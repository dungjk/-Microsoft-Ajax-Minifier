// AnalyzeNodeVisitor.cs
//
// Copyright 2011 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Ajax.Utilities
{
    public class ResolutionVisitor : IVisitor
    {
        #region private fields 

        // index to use for ordering the statements in this scope
        private long m_orderIndex;

        // flag indicating that the current block has had a terminating statement
        // and that all following statements are unreachable.
        private bool m_isUnreachable;

        // there is only one variable scope for this run
        private ActivationObject m_variableScope;

        // but we might have a stack of lexical scopes to keep track of which one is currently active
        private Stack<ActivationObject> m_lexicalStack;

        // list of child functions that we need to process
        private IList<FunctionObject> m_childFunctions;

        private IList<ParameterDeclaration> m_catchParameters;

        private HashSet<VariableDeclaration> m_vardeclsInsideWith;

        private int m_withDepth;

        private CodeSettings m_settings;

        #endregion

        #region private properties

        private ActivationObject CurrentLexicalScope
        {
            get
            {
                return m_lexicalStack.Peek();
            }
        }

        private long NextOrderIndex
        {
            get
            {
                return m_isUnreachable ? 0 : ++m_orderIndex;
            }
        }

        #endregion

        #region constructor

        private ResolutionVisitor(ActivationObject variableScope, CodeSettings settings)
        {
            m_settings = settings;
            m_variableScope = variableScope;

            m_lexicalStack = new Stack<ActivationObject>();
            m_lexicalStack.Push(variableScope);

            // list of child functions we will want to process later
            m_childFunctions = new List<FunctionObject>();

            // list of catch parameters we will need to check for possible IE collisions
            m_catchParameters = new List<ParameterDeclaration>();

            // for any vardecls inside a with statement with an initializer we need to
            // make sure the field we generate isn't automatically renamed.
            m_vardeclsInsideWith = new HashSet<VariableDeclaration>();
        }

        public static void Apply(AstNode node, ActivationObject scope, CodeSettings settings)
        {
            if (node != null)
            {
                // check the two possible declarations we might have here, and if 
                // the node is one of them, we'll need to declare them in the scope before
                // we start to process them.
                var funcObject = node as FunctionObject;
                if (funcObject != null)
                {
                    DeclareFunction(funcObject, scope);
                }
                else
                {
                    var varDecl = node as VariableDeclaration;
                    if (varDecl != null)
                    {
                        DeclareVariable(varDecl, scope, false);
                    }
                }

                // start the process
                BuildScope(node, scope, null, settings);
            }
        }

        #endregion

        #region private static methods

        private static void DeclareFunction(FunctionObject funcDecl, ActivationObject scope)
        {
            // add it if it isn't there already
            var funcField = scope[funcDecl.Name];
            if (funcField == null)
            {
                // could be global or local function, depending on the scope
                funcField = scope.CreateField(funcDecl.Name, null, 0);
                scope.AddField(funcField);
            }

            // always clobber the original context because a function declaration will
            // take precedence over a parameter or a previous function declaration.
            funcField.OriginalContext = funcDecl.IdContext;
            funcField.IsDeclared = true;
            funcField.IsFunction = true;

            // set the value of the field to be the function object
            funcField.FieldValue = funcDecl;

            funcDecl.VariableField = funcField;
            funcDecl.Identifier.IfNotNull(i => i.VariableField = funcField);
        }

        private static void DeclareVariable(VariableDeclaration varDecl, ActivationObject scope, bool isInsideWithStatement)
        {
            var field = scope[varDecl.Name];
            if (field == null)
            {
                // could be global or local depending on the scope, so let the scope create it.
                field = scope.CreateField(varDecl.Name, null, 0);
                field.OriginalContext = varDecl.IdentifierContext;
                field.IsDeclared = true;

                if (varDecl.Initializer != null && isInsideWithStatement)
                {
                    // this vardecl is inside a with-statement and has an initializer. We need
                    // to make sure it doesn't get automatically renamed
                    field.CanCrunch = false;
                }

                scope.AddField(field);
            }
            else if (varDecl.Initializer != null)
            {
                // already defined! if this is an initialized var, then the var part is
                // superfluous and the "initializer" is really a lookup assignment. 
                // So bump up the ref-count for those cases.
                field.AddReference(varDecl);
            }

            varDecl.Field = field;
        }

        private static void BuildScope(AstNode node, ActivationObject containingScope, AstNodeList parameterDeclarations, CodeSettings settings)
        {
            var visitor = new ResolutionVisitor(containingScope, settings); 
            node.Accept(visitor);

            // create the declarations for this scope
            visitor.CreateDeclarations(parameterDeclarations);

            // now create all the child function scopes so we can know which ones are
            // referenced or not.
            visitor.CreateChildFunctionScopes();

            // resolve the lookups in our scope, which will recurse
            // any lexical scopes we may have found processing the node tree
            // (not counting function scopes)
            visitor.ResolveLookups();

            // now go through the child functions and do the same thing for them
            visitor.RecurseChildFunctionScopes();

            // once I get here, my scope and all my child scopes have had all
            // their standard objects declared and all their lookups resolved.
            // this will satisfy the ecmascript language specifications.
            // however, now we want to add in the processing we need to do to 
            // also be in sync with older IE (IE8 and below) scoping anomolies.
            visitor.ResolveGhostCatchParameters();
            visitor.ResolveGhostFunctionExpressions();
        }

        #endregion

        #region private methods

        private void CreateChildFunctionScopes()
        {
            // finally, setup any child function scopes using a separate visitor
            foreach (var funcObject in m_childFunctions)
            {
                // if we haven't already created the function scope, do it now
                if (funcObject.FunctionScope == null)
                {
                    // by default, the child functions create a scope under the variable scope.
                    // but named function expressions will create an interstitial scope containing just
                    // the function name, which will lay between the variable scope and the function scope.
                    var parentScope = m_variableScope;

                    // if this is a named function expression, we need to create two scopes: one for the
                    // name of the function, and another for the function's variable environment.
                    if (funcObject.FunctionType == FunctionType.Expression && funcObject.Identifier != null)
                    {
                        // create a function scope just for the name
                        parentScope = new FunctionScope(parentScope, true, funcObject.Parser) { FunctionObject = funcObject };

                        // add a field in the child scope for the function expression name so it can be self-referencing.
                        var functionField = parentScope.CreateField(funcObject.Name, funcObject, 0);
                        functionField.IsFunction = true;
                        functionField.OriginalContext = funcObject.IdContext;

                        funcObject.VariableField = functionField;
                        funcObject.Identifier.VariableField = functionField;

                        parentScope.AddField(functionField);
                    }

                    // create the function's variable scope and recurse
                    funcObject.FunctionScope = new FunctionScope(parentScope, funcObject.FunctionType != FunctionType.Declaration, funcObject.Parser)
                    {
                        FunctionObject = funcObject
                    };
                }
            }
        }

        private void RecurseChildFunctionScopes()
        {
            foreach(var funcObject in m_childFunctions)
            {
                // set up this function's scope
                BuildScope(funcObject.Body, funcObject.FunctionScope, funcObject.ParameterDeclarations, m_settings);
            }
        }

        private void CreateDeclarations(AstNodeList parameterDeclarations)
        {
            // first bind any parameters
            DeclareParameters(parameterDeclarations);

            // bind function declarations next
            DeclareFunctions();

            // bind the arguments object if this is a function scope
            DeclareArgumentsObject();

            // and finally, the variable declarations
            DeclareVariables();
        }

        #endregion

        #region private declaration methods

        private void DeclareParameters(AstNodeList parameterDeclarations)
        {
            if (parameterDeclarations != null)
            {
                foreach (ParameterDeclaration parameter in parameterDeclarations)
                {
                    var argumentField = m_variableScope[parameter.Name];
                    if (argumentField == null)
                    {
                        argumentField = new JSVariableField(FieldType.Argument, parameter.Name, 0, null)
                        {
                            Position = parameter.Position,
                            OriginalContext = parameter.Context
                        };

                        m_variableScope.AddField(argumentField);
                    }

                    parameter.Field = argumentField;
                }
            }
        }

        private void DeclareFunctions()
        {
            foreach (var funcDecl in m_variableScope.FunctionDeclarations)
            {
                DeclareFunction(funcDecl, m_variableScope);
            }
        }

        private void DeclareArgumentsObject()
        {
            if (m_variableScope is FunctionScope)
            {
                if (m_variableScope["arguments"] == null)
                {
                    m_variableScope.AddField(new JSVariableField(FieldType.Arguments, "arguments", 0, null));
                }
            }
        }

        private void DeclareVariables()
        {
            foreach (var varDecl in m_variableScope.VariableDeclarations)
            {
                DeclareVariable(varDecl, m_variableScope, m_vardeclsInsideWith.Contains(varDecl));
            }
        }

        #endregion

        #region private resolution methods

        private void ResolveLookups()
        {
            // kick off the processing with our variable scope and the current settings
            ResolveLookups(m_variableScope, m_settings);
        }

        private static void ResolveLookups(ActivationObject lexicalScope, CodeSettings settings)
        {
            // all the declarations are in place. Now we can go through the scope tree and resolve all their references
            foreach (var lookup in lexicalScope.ScopeLookups)
            {
                // resolve lookup via the lexical scope
                lookup.VariableField = lexicalScope.FindReference(lookup.Name);
                if (lookup.VariableField.FieldType == FieldType.UndefinedGlobal)
                {
                    // couldn't find it.
                    // if the lookup isn't generated and isn't the object of a typeof operator,
                    // then we want to throw an error.
                    UnaryOperator unaryOperator;
                    if (!lookup.IsGenerated
                        && ((unaryOperator = lookup.Parent as UnaryOperator) == null || unaryOperator.OperatorToken != JSToken.TypeOf))
                    {
                        // report this undefined reference
                        lookup.Context.ReportUndefined(lookup);

                        // possibly undefined global (but definitely not local).
                        // see if this is a function or a variable.
                        var callNode = lookup.Parent as CallNode;
                        var isFunction = callNode != null && callNode.Function == lookup;
                        lookup.Context.HandleError((isFunction ? JSError.UndeclaredFunction : JSError.UndeclaredVariable), false);
                    }
                }
                else if (lookup.VariableField.FieldType == FieldType.Predefined)
                {
                    // check to see if this is the eval function -- if so, mark the scope as not-known if we aren't ignoring them
                    if (settings.EvalTreatment != EvalTreatment.Ignore
                        && string.CompareOrdinal(lookup.Name, "eval") == 0)
                    {
                        // it's an eval -- but are we calling it?
                        // TODO: what if we are assigning it to a variable? Should we track that variable and see if we call it?
                        // What about passing it as a parameter to a function? track that as well in case the function calls it?
                        var parentCall = lookup.Parent as CallNode;
                        if (parentCall != null && parentCall.Function == lookup)
                        {
                            lexicalScope.IsKnownAtCompileTime = false;
                        }
                    }
                }
                else
                {
                    // TODO: any other checks?
                }

                // add the reference
                lookup.VariableField.AddReference(lookup);

                // we are actually referencing this field, so it's no longer a placeholder field if it
                // happens to have been one.
                lookup.VariableField.IsPlaceholder = false;
            }

            // recurse any lexical scopes within this function
            foreach (var childScope in lexicalScope.ChildScopes)
            {
                ResolveLookups(childScope, settings);
            }
        }

        private void ResolveGhostCatchParameters()
        {
            foreach (var catchParameter in m_catchParameters)
            {
                // check to see if the name exists in the outer variable scope.
                var ghostField = m_variableScope[catchParameter.Name];
                if (ghostField == null)
                {
                    // set up a ghost field to keep track of the relationship
                    ghostField = new JSVariableField(FieldType.GhostCatch, catchParameter.Name, 0, null)
                    {
                        OriginalContext = catchParameter.Context
                    };

                    m_variableScope.AddField(ghostField);
                }
                else if (ghostField.FieldType == FieldType.GhostCatch)
                {
                    // there is, but it's another ghost catch variable. That's fine; just use it.
                    // don't even flag it as ambiguous because if someone is later referencing the error variable
                    // used in a couple catch variables, we'll say something then because other browsers will have that
                    // variable undefined or from an outer scope.
                }
                else
                {
                    // there is, and it's NOT another ghosted catch variable. Possible naming
                    // collision in IE -- if an error happens, it will clobber the existing field's value,
                    // although that MAY be the intention; we don't know for sure. But it IS a cross-
                    // browser behavior difference.
                    ghostField.IsAmbiguous = true;

                    if (ghostField.OuterField != null)
                    {
                        // and to make matters worse, it's actually bound to an OUTER field
                        // in modern browsers, but will bind to this catch variable in older
                        // versions of IE! Definitely a cross-browser difference!
                        // throw a cross-browser issue error.
                        catchParameter.Context.HandleError(JSError.AmbiguousCatchVar);
                    }
                }

                // link them so they all keep the same name going forward
                // (since they are named the same in the sources)
                catchParameter.Field.OuterField = ghostField;

                // TODO: this really should be a LIST of ghosted fields, since multiple 
                // elements can ghost to the same field.
                ghostField.GhostedField = catchParameter.Field;

                // if the actual field has references, we want to bubble those up
                // since we're now linking those fields
                if (catchParameter.Field.RefCount > 0)
                {
                    // add the catch parameter's references to the ghost field
                    ghostField.AddReferences(catchParameter.Field.References);
                }
            }
        }

        private void ResolveGhostFunctionExpressions()
        {
            foreach (var funcObject in m_childFunctions)
            {
                // only interested in the named function expressions
                if (funcObject.FunctionType == FunctionType.Expression && funcObject.Identifier != null)
                {
                    var functionField = funcObject.VariableField;

                    // let's check on ghosted names in the outer variable scope
                    var ghostField = m_variableScope[funcObject.Name];
                    if (ghostField == null)
                    {
                        // nothing; good to go. Add a ghosted field to keep track of it.
                        ghostField = new JSVariableField(FieldType.GhostFunctionExpression, funcObject.Name, 0, funcObject)
                        {
                            OriginalContext = functionField.OriginalContext
                        };

                        m_variableScope.AddField(ghostField);
                    }
                    else if (ghostField.FieldType == FieldType.GhostFunctionExpression)
                    {
                        // there is, but it's another ghosted function expression.
                        // what if a lookup is resolved to this field later? We probably still need to
                        // at least flag it as ambiguous. We will only need to throw an error, though,
                        // if someone actually references the outer ghost variable. 
                        ghostField.IsAmbiguous = true;
                    }
                    else
                    {
                        // something already exists. Could be a naming collision for IE or at least a
                        // a cross-browser behavior difference if it's not coded properly.
                        // mark this field as a function, even if it wasn't before
                        ghostField.IsFunction = true;

                        if (ghostField.OuterField != null)
                        {
                            // if the pre-existing field we are ghosting is a reference to
                            // an OUTER field, then we actually have a problem that creates a BIG
                            // difference between older IE browsers and everything else.
                            // modern browsers will have the link to the outer field, but older
                            // IE browsers will link to this function expression!
                            // fire a cross-browser error warning
                            ghostField.IsAmbiguous = true;
                            funcObject.IdContext.HandleError(JSError.AmbiguousNamedFunctionExpression);
                        }
                        else if (ghostField.IsReferenced)
                        {
                            // if the ghosted field isn't even referenced, then who cares?
                            // but it is referenced. Let's see if it matters.
                            // something like: var nm = function nm() {}
                            // is TOTALLY cool common cross-browser syntax.
                            var parentVarDecl = funcObject.Parent as VariableDeclaration;
                            if (parentVarDecl == null
                                || parentVarDecl.Name != funcObject.Name)
                            {
                                // see if it's a simple assignment.
                                // something like: var nm; nm = function nm(){},
                                // would also be cool, although less-common than the vardecl version.
                                Lookup lookup;
                                var parentAssignment = funcObject.Parent as BinaryOperator;
                                if (parentAssignment == null || parentAssignment.OperatorToken != JSToken.Assign
                                    || parentAssignment.Operand2 != funcObject
                                    || (lookup = parentAssignment.Operand1 as Lookup) == null
                                    || lookup.Name != funcObject.Name)
                                {
                                    // something else. Flag it as ambiguous.
                                    ghostField.IsAmbiguous = true;
                                }
                            }
                        }
                    }

                    // link them so they all keep the same name going forward
                    // (since they are named the same in the sources)
                    functionField.OuterField = ghostField;

                    // TODO: this really should be a LIST of ghosted fields, since multiple 
                    // elements can ghost to the same field.
                    ghostField.GhostedField = functionField;

                    // if the actual field has references, we want to bubble those up
                    // since we're now linking those fields
                    if (functionField.RefCount > 0)
                    {
                        // add the function's references to the ghost field
                        ghostField.AddReferences(functionField.References);
                    }
                }
            }
        }

        #endregion

        #region IVisitor Members

        public void Visit(ArrayLiteral node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Elements != null)
                {
                    node.Elements.Accept(this);
                }
            }
        }

        public void Visit(AspNetBlockNode node)
        {
            // nothing to do
        }

        public void Visit(AstNodeList node)
        {
            if (node != null)
            {
                // don't bother setting the order of the list itself, just the items
                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    var item = node[ndx];
                    if (item != null)
                    {
                        item.Accept(this);
                    }
                }
            }
        }

        public void Visit(BinaryOperator node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Operand1 != null)
                {
                    node.Operand1.Accept(this);
                }

                if (node.Operand2 != null)
                {
                    node.Operand2.Accept(this);
                }
            }
        }

        public void Visit(Block node)
        {
            if (node != null)
            {
                // TODO: this could create a scope if it contains ES6 const or let statements.

                // don't bother setting the order of the block itself, just its children
                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    var statement = node[ndx];
                    if (statement != null)
                    {
                        statement.Accept(this);
                    }
                }

                // be sure to reset the unreachable flag when we exit this block
                m_isUnreachable = false;
            }
        }

        public void Visit(Break node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;

                // we can stop marking order for subsequent statements in this block,
                // since this stops execution
                m_isUnreachable = true;
            }
        }

        public void Visit(CallNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Function != null)
                {
                    node.Function.Accept(this);
                }

                if (node.Arguments != null)
                {
                    node.Arguments.Accept(this);
                }
            }
        }

        public void Visit(ConditionalCompilationComment node)
        {
            if (node != null)
            {
                if (node.Statements != null)
                {
                    node.Statements.Accept(this);
                }
            }
        }

        public void Visit(ConditionalCompilationElse node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConditionalCompilationElseIf node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConditionalCompilationEnd node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConditionalCompilationIf node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConditionalCompilationOn node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConditionalCompilationSet node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(Conditional node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }

                if (node.TrueExpression != null)
                {
                    node.TrueExpression.Accept(this);
                }

                if (node.FalseExpression != null)
                {
                    node.FalseExpression.Accept(this);
                }
            }
        }

        public void Visit(ConstantWrapper node)
        {
            if (node != null)
            {
                // no execution step for literals.
            }
        }

        public void Visit(ConstantWrapperPP node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ConstStatement node)
        {
            if (node != null)
            {
                // the statement itself doesn't get executed, but the initializers do
                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    var item = node[ndx];
                    if (item != null)
                    {
                        item.Accept(this);
                    }
                }
            }
        }

        public void Visit(ContinueNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;

                // we can stop marking order for subsequent statements in this block,
                // since this stops execution
                m_isUnreachable = true;
            }
        }

        public void Visit(CustomNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
            }
        }

        public void Visit(DebuggerNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
            }
        }

        public void Visit(DirectivePrologue node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.UseStrict)
                {
                    m_variableScope.UseStrict = true;
                }
            }
        }

        public void Visit(DoWhile node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Body != null)
                {
                    node.Body.Accept(this);
                }

                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
            }
        }

        public void Visit(ForIn node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Variable != null)
                {
                    node.Variable.Accept(this);
                }

                if (node.Collection != null)
                {
                    node.Collection.Accept(this);
                }

                if (node.Body != null)
                {
                    node.Body.Accept(this);
                }
            }
        }

        public void Visit(ForNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Initializer != null)
                {
                    node.Initializer.Accept(this);
                }

                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }

                if (node.Body != null)
                {
                    node.Body.Accept(this);
                }

                if (node.Incrementer != null)
                {
                    node.Incrementer.Accept(this);
                }
            }
        }

        public void Visit(FunctionObject node)
        {
            if (node != null)
            {
                // DON'T RECURSE NOW -- save the node for later.
                if (node.FunctionType == FunctionType.Declaration)
                {
                    // we will declare these in the variable scope
                    m_variableScope.FunctionDeclarations.Add(node);
                }

                // these are just for recursing later
                m_childFunctions.Add(node);
            }
        }

        public void Visit(GetterSetter node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(IfNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }

                // make true and false block numbered from the same starting point?
                var startingPoint = m_orderIndex;
                if (node.TrueBlock != null)
                {
                    node.TrueBlock.Accept(this);
                }

                var trueStop = m_orderIndex;
                m_orderIndex = startingPoint;

                if (node.FalseBlock != null)
                {
                    node.FalseBlock.Accept(this);
                }

                // and keep counting from the farthest point
                if (trueStop > m_orderIndex)
                {
                    m_orderIndex = trueStop;
                }
            }
        }

        public void Visit(ImportantComment node)
        {
            if (node != null)
            {
                // nothing to do.
            }
        }

        public void Visit(LabeledStatement node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Statement != null)
                {
                    node.Statement.Accept(this);
                }
            }
        }

        public void Visit(Lookup node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;

                // save the lookup to the current lexical scope
                CurrentLexicalScope.ScopeLookups.Add(node);
            }
        }

        public void Visit(Member node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Root != null)
                {
                    node.Root.Accept(this);
                }
            }
        }

        public void Visit(ObjectLiteral node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                foreach (var item in node.Values)
                {
                    item.Accept(this);
                }
            }
        }

        public void Visit(ObjectLiteralField node)
        {
            if (node != null)
            {
                // nothing to do
            }
        }

        public void Visit(ParameterDeclaration node)
        {
            if (node != null)
            {
                // do nothing
            }
        }

        public void Visit(RegExpLiteral node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
            }
        }

        public void Visit(ReturnNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Operand != null)
                {
                    node.Operand.Accept(this);
                }

                // we can stop marking order for subsequent statements in this block,
                // since this stops execution
                m_isUnreachable = true;
            }
        }

        public void Visit(Switch node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Expression != null)
                {
                    node.Expression.Accept(this);
                }

                if (node.Cases != null)
                {
                    node.Cases.Accept(this);
                }
            }
        }

        public void Visit(SwitchCase node)
        {
            if (node != null)
            {
                if (node.Statements != null)
                {
                    node.Statements.Accept(this);
                }
            }
        }

        public void Visit(ThisLiteral node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
            }
        }

        public void Visit(ThrowNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Operand != null)
                {
                    node.Operand.Accept(this);
                }

                // we can stop marking order for subsequent statements in this block,
                // since this stops execution
                m_isUnreachable = true;
            }
        }

        public void Visit(TryNode node)
        {
            if (node != null)
            {
                // if there is a catch parameter, then we will need to check for possible
                // IE collisions in the variable scope later, once all the declarations have
                // been processed
                if (node.CatchParameter != null)
                {
                    m_catchParameters.Add(node.CatchParameter);
                }

                node.Index = NextOrderIndex;
                if (node.TryBlock != null)
                {
                    node.TryBlock.Accept(this);
                }

                if (node.CatchBlock != null)
                {
                    // create the catch scope, push it on the stack, and process the
                    // body. don't forget to pop it off
                    var catchScope = new CatchScope(CurrentLexicalScope, node.CatchBlock.Context, node.Parser);
                    node.CatchBlock.BlockScope = catchScope;
                    m_lexicalStack.Push(catchScope);
                    try
                    {
                        // add it to the catch-scope's name table
                        if (node.CatchParameter != null)
                        {
                            var catchField = new JSVariableField(FieldType.Argument, node.CatchParameter.Name, 0, null);
                            catchScope.AddField(catchField);
                            catchField.OriginalContext = node.CatchParameter.IfNotNull(p => p.Context);
                            node.CatchParameter.Field = catchScope.CatchField = catchField;
                        }

                        node.CatchBlock.Accept(this);
                    }
                    finally
                    {
                        m_lexicalStack.Pop();
                    }
                }

                if (node.FinallyBlock != null)
                {
                    node.FinallyBlock.Accept(this);
                }
            }
        }

        public void Visit(UnaryOperator node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Operand != null)
                {
                    node.Operand.Accept(this);
                }
            }
        }

        public void Visit(Var node)
        {
            if (node != null)
            {
                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    var decl = node[ndx];
                    if (decl != null)
                    {
                        decl.Accept(this);
                    }
                }
            }
        }

        public void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                // we really should do something different for var, const, and let
                // because in ES6 the const and let declarations are block-specific.
                // but for now, just save them
                m_variableScope.VariableDeclarations.Add(node);

                if (node.Initializer != null)
                {
                    // the declaration only gets executed if it has an initializer
                    node.Index = NextOrderIndex;
                    node.Initializer.Accept(this);

                    // if this vardecl with an assignment happens to be inside a
                    // with-statement, then we need to treat the initialization as
                    // the run-time lookup/assignment it really is. Which means,
                    // because the with-statement is a special scope, we will need
                    // to create an inner reference within the with-scope pointing
                    // to this var's declared field.
                    if (m_withDepth > 0)
                    {
                        // add it to this set; we'll deal with it later
                        m_vardeclsInsideWith.Add(node);
                    }
                }
            }
        }

        public void Visit(WhileNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }

                if (node.Body != null)
                {
                    node.Body.Accept(this);
                }
            }
        }

        public void Visit(WithNode node)
        {
            if (node != null)
            {
                node.Index = NextOrderIndex;
                if (node.WithObject != null)
                {
                    node.WithObject.Accept(this);
                }

                if (node.Body != null)
                {
                    // create the scope for the with-statement
                    var withScope = new WithScope(CurrentLexicalScope, node.Context, node.Parser);
                    node.Body.BlockScope = withScope;

                    // push it on the stack, recurse the body, then pop it off
                    m_lexicalStack.Push(withScope);
                    try
                    {
                        ++m_withDepth;
                        node.Body.Accept(this);
                    }
                    finally
                    {
                        --m_withDepth;
                        m_lexicalStack.Pop();
                    }
                }
            }
        }

        #endregion
    }
}
