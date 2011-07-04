// functionobject.cs
//
// Copyright 2010 Microsoft Corporation
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
using System.Reflection;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class FunctionObject : AstNode
    {
        private Block m_body;
        public Block Body
        {
            get { return m_body; }
            set
            {
                if (value != m_body)
                {
                    if (m_body != null && m_body.Parent == this)
                    {
                        m_body.Parent = null;
                    }
                    m_body = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public FunctionType FunctionType { get; private set; }

        private List<ParameterDeclaration> m_parameterDeclarations;
        public IList<ParameterDeclaration> ParameterDeclarations
        {
            get
            {
                return m_parameterDeclarations;
            }
        }

        private bool m_leftHandFunction;// = false;
        public bool LeftHandFunctionExpression
        {
            get
            {
                return (FunctionType == FunctionType.Expression && m_leftHandFunction);
            }
            set
            {
                m_leftHandFunction = value;
            }
        }

        public string Name { get; set; }
        public Context IdContext { get; set; }
        public bool NameError { get; set; }

        public override bool IsExpression
        {
            get
            {
                // if this is a declaration, then it's not an expression. Otherwise treat it 
                // as if it were an expression.
                return !(FunctionType == FunctionType.Declaration);
            }
        }

        private Binding m_binding;
        public Binding Binding 
        {
            get
            {
                // don't create bindings for getter/setter, or for functions without names
                if (m_binding == null
                    && Parent != null
                    && !string.IsNullOrEmpty(Name)
                    && (FunctionType == FunctionType.Declaration || FunctionType == FunctionType.Expression))
                {
                    // get the parent variable environment, if we can
                    var variableEnvironment = Parent.EnclosingVariableEnvironment;
                    if (variableEnvironment != null)
                    {
                        // see if the function name is already bound in the parent
                        if (!variableEnvironment.TryGetBinding(Name, out m_binding))
                        {
                            // doesn't exist. If this is a function declaration, we want to create it
                            // as a normal name. If it's a function expression, we want to create a special
                            // NFE field.
                            m_binding = variableEnvironment.CreateMutableBinding(Name, false);
                            m_binding.Category = FunctionType == FunctionType.Declaration
                                ? BindingCategory.Normal
                                : BindingCategory.NamedFunctionExpression;
                        }
                        else if (FunctionType == FunctionType.Expression)
                        {
                            // already exists -- if this field isn't already ambiguous, save us as the ambiguous
                            // value and move on
                            m_binding.AmbiguousValue = this;
                        }
                    }
                }
                return m_binding;
            }
            set { m_binding = value; }
        }
        
        public int ReferenceCount
        {
            get
            {
                return Binding != null ? Binding.ReferenceCount : -1;
            }
        }

        public DeclarativeEnvironment LexicalEnvironment { get; set; }
        public override LexicalEnvironment EnclosingLexicalEnvironment
        {
            get
            {
                // return our lexical environment
                return LexicalEnvironment;
            }
        }

        public override LexicalEnvironment EnclosingVariableEnvironment
        {
            get
            {
                // return our lexical environment (it's both variable- and lexical-environment for functions)
                return LexicalEnvironment;
            }
        }

        public FunctionObject(Lookup identifier, JSParser parser, FunctionType functionType, IList<ParameterDeclaration> parameterDeclarations, Block bodyBlock, Context functionContext)
            : base(functionContext, parser)
        {
            FunctionType = functionType;
            Body = bodyBlock;

            // if we were passed a list, copy it now
            if (parameterDeclarations != null)
            {
                m_parameterDeclarations = new List<ParameterDeclaration>(parameterDeclarations);
            }

            if (identifier != null)
            {
                Name = identifier.Name;
                IdContext = identifier.Context;
            }

            /*
            // now we need to make sure that the enclosing scope has the name of this function defined
            // so that any references get properly resolved once we start analyzing the parent scope
            // see if this is not anonymnous AND not a getter/setter
            bool isGetterSetter = (FunctionType == FunctionType.Getter || FunctionType == FunctionType.Setter);
            if (Identifier != null && !isGetterSetter)
            {
                // yes -- add the function name to the current enclosing
                // check whether the function name is in use already
                // shouldn't be any duplicate names
                ActivationObject enclosingScope = m_functionScope.Parent;
                // functions aren't owned by block scopes
                while (enclosingScope is BlockScope)
                {
                    enclosingScope = enclosingScope.Parent;
                }

                // if the enclosing scope already contains this name, then we know we have a dup
                string functionName = Identifier.Name;
                m_variableField = enclosingScope[functionName];
                if (m_variableField != null)
                {
                    // it's pointing to a function
                    m_variableField.IsFunction = true;

                    if (FunctionType == FunctionType.Expression)
                    {
                        // if the containing scope is itself a named function expression, then just
                        // continue on as if everything is fine. It will chain and be good.
                        if (!(m_variableField is JSNamedFunctionExpressionField))
                        {
                            if (m_variableField.NamedFunctionExpression != null)
                            {
                                // we have a second named function expression in the same scope
                                // with the same name. Not an error unless someone actually references
                                // it.

                                // we are now ambiguous.
                                m_variableField.IsAmbiguous = true;

                                // BUT because this field now points to multiple function object, we
                                // need to break the connection. We'll leave the inner NFEs pointing
                                // to this field as the outer field so the names all align, however.
                                DetachFromOuterField(true);

                                // create a new NFE pointing to the existing field as the outer so
                                // the names stay in sync, and with a value of our function object.
                                JSNamedFunctionExpressionField namedExpressionField = 
                                    new JSNamedFunctionExpressionField(m_variableField);
                                namedExpressionField.FieldValue = this;
                                m_functionScope.AddField(namedExpressionField);

                                // hook our function object up to the named field
                                m_variableField = namedExpressionField;
                                Identifier.VariableField = namedExpressionField;

                                // we're done; quit.
                                return;
                            }
                            else if (m_variableField.IsAmbiguous)
                            {
                                // we're pointing to a field that is already marked as ambiguous.
                                // just create our own NFE pointing to this one, and hook us up.
                                JSNamedFunctionExpressionField namedExpressionField = 
                                    new JSNamedFunctionExpressionField(m_variableField);
                                namedExpressionField.FieldValue = this;
                                m_functionScope.AddField(namedExpressionField);

                                // hook our function object up to the named field
                                m_variableField = namedExpressionField;
                                Identifier.VariableField = namedExpressionField;

                                // we're done; quit.
                                return;
                            }
                            else
                            {
                                // we are a named function expression in a scope that has already
                                // defined a local variable of the same name. Not good. Throw the 
                                // error but keep them attached because the names have to be synced
                                // to keep the same meaning in all browsers.
                                Identifier.Context.HandleError(JSError.AmbiguousNamedFunctionExpression, false);

                                // if we are preserving function names, then we need to mark this field
                                // as not crunchable
                                if (Parser.Settings.PreserveFunctionNames)
                                {
                                    m_variableField.CanCrunch = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        // function declaration -- duplicate name
                        Identifier.Context.HandleError(JSError.DuplicateName, false);
                    }
                }
                else
                {
                    // doesn't exist -- create it now
                    m_variableField = enclosingScope.DeclareField(functionName, this, 0);

                    // and it's a pointing to a function object
                    m_variableField.IsFunction = true;
                }

                // set the identifier variable field now. We *know* what the field is now, and during
                // Analyze mode we aren't going to recurse into the identifier because that would add 
                // a reference to it.
                Identifier.VariableField = m_variableField;

                // if we're here, we have a name. if this is a function expression, then we have
                // a named function expression and we need to do a little more work to prepare for
                // the ambiguities of named function expressions in various browsers.
                if (FunctionType == FunctionType.Expression)
                {
                    // now add a field within the function scope that indicates that it's okay to reference
                    // this named function expression from WITHIN the function itself.
                    // the inner field points to the outer field since we're going to want to catch ambiguous
                    // references in the future
                    JSNamedFunctionExpressionField namedExpressionField = new JSNamedFunctionExpressionField(m_variableField);
                    m_functionScope.AddField(namedExpressionField);
                    m_variableField.NamedFunctionExpression = namedExpressionField;
                }
                else
                {
                    // function declarations are declared by definition
                    m_variableField.IsDeclared = true;
                }
            }*/
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Body == oldNode)
            {
                Body = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        //private bool HideFromOutput
        //{
        //    get
        //    {
        //        // don't remove function expressions or getter/setters;
        //        // don't remove global functions; and if the scope is
        //        // unknown, then we can't remove it either, because we don't know
        //        // what the unknown code might call.
        //        return (FunctionType == FunctionType.Declaration 
        //            && Binding != null 
        //            && !Binding.IsReferenced 
        //            && LexicalEnvironment.Outer.IsKnownAtCompileTime 
        //            && Parser.Settings.RemoveUnneededCode);
        //    }
        //}

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    if (!Parser.Settings.MinifyCode || !HideFromOutput)
        //    {
        //        if (LeftHandFunctionExpression)
        //        {
        //            sb.Append('(');
        //        }
        //        if (format != ToCodeFormat.NoFunction)
        //        {
        //            sb.Append("function");
        //            if (!string.IsNullOrEmpty(Name))
        //            {
        //                // we don't want to show the name for named function expressions where the
        //                // name is never referenced. Don't use IsReferenced because that will always
        //                // return true for function expressions. Since we really only want to know if
        //                // the field name is referenced, check the refcount directly on the field.
        //                // also output the function expression name if this is debug mode
        //                if (FunctionType != FunctionType.Expression
        //                    || !(Parser.Settings.RemoveFunctionExpressionNames && Parser.Settings.IsModificationAllowed(TreeModifications.RemoveFunctionExpressionNames))
        //                    || Binding.ReferenceCount > 0)
        //                {
        //                    sb.Append(' ');
        //                    sb.Append(Binding.ToString());
        //                }
        //            }
        //        }

        //        if (m_parameterDeclarations != null)
        //        {
        //            sb.Append('(');
        //            if (m_parameterDeclarations.Count > 0)
        //            {
        //                // figure out the last referenced argument so we can skip
        //                // any that aren't actually referenced
        //                int lastRef = m_parameterDeclarations.Count - 1;

        //                // if we're not known at compile time, then we can't leave off unreferenced parameters
        //                // (also don't leave things off if we're not hypercrunching)
        //                // (also check the kill flag for removing unused parameters)
        //                if (Parser.Settings.RemoveUnneededCode
        //                    && LexicalEnvironment.IsKnownAtCompileTime
        //                    && Parser.Settings.MinifyCode
        //                    && Parser.Settings.IsModificationAllowed(TreeModifications.RemoveUnusedParameters))
        //                {
        //                    while (lastRef >= 0)
        //                    {
        //                        // we want to loop backwards until we either find a parameter that is referenced.
        //                        // at that point, lastRef will be the index of the last referenced parameter so
        //                        // we can output from 0 to lastRef
        //                        Binding binding = m_parameterDeclarations[lastRef].Binding;
        //                        if (binding != null && !binding.IsReferenced)
        //                        {
        //                            --lastRef;
        //                        }
        //                        else
        //                        {
        //                            // found a referenced parameter, or something weird -- stop looking
        //                            break;
        //                        }
        //                    }
        //                }

        //                for (int ndx = 0; ndx <= lastRef; ++ndx)
        //                {
        //                    if (ndx > 0)
        //                    {
        //                        sb.Append(',');
        //                    }
        //                    sb.Append(m_parameterDeclarations[ndx].Name);
        //                }
        //            }

        //            sb.Append(')');
        //        }

        //        if (Body != null)
        //        {
        //            if (Body.Count == 0)
        //            {
        //                sb.Append("{}");
        //            }
        //            else
        //            {
        //                sb.Append(Body.ToCode(ToCodeFormat.AlwaysBraces));
        //            }
        //        }

        //        if (LeftHandFunctionExpression)
        //        {
        //            sb.Append(')');
        //        }
        //    }
        //    return sb.ToString();
        //}

        public override bool RequiresSeparator
        {
            get { return false; }
        }

        public Binding AddGeneratedVar(string name, AstNode initializer)
        {
            // if the body is empty, create one now
            if (Body == null)
            {
                Body = new Block(null, Parser);
            }

            VarStatement varStatement = null;
            ForStatement forStatement = null;

            var ndxFirst = 0;
            if (Body.Count > 0)
            {
                // find the first index of a statement that ISN'T a function declaration, an important comment, or a declaration prologue
                FunctionObject funcObj;
                while (ndxFirst < Body.Count)
                {
                    if (Body[ndxFirst].IsDirectivePrologue
                        || Body[ndxFirst] is ImportantComment
                        || ((funcObj = Body[ndxFirst] as FunctionObject) != null && funcObj.FunctionType == FunctionType.Declaration))
                    {
                        ++ndxFirst;
                    }
                    else
                    {
                        break;
                    }
                }

                if (ndxFirst < Body.Count)
                {
                    // if the first statement is a var-statement, we'll put it there
                    varStatement = Body[ndxFirst] as VarStatement;
                    if (varStatement == null)
                    {
                        // it's not a var; see if the first statement is a for-statement
                        forStatement = Body[ndxFirst] as ForStatement;
                        if (forStatement != null)
                        {
                            // it is! Now see if the incrementer is a var. If it is, we'll just stick
                            // it in there.
                            varStatement = forStatement.Initializer as VarStatement;
                        }
                    }
                }
            }

            // create the binding first, just to make sure we get it right and with a unique name
            var uniqueName = name;
            int suffix = 0;
            while (LexicalEnvironment.HasBinding(uniqueName))
            {
                uniqueName = name + (suffix++).ToString(CultureInfo.InvariantCulture);
            }

            // make it an immutable binding -- since we are creating it, we won't reuse it
            var binding = LexicalEnvironment.CreateImmutableBinding(uniqueName);
            LexicalEnvironment.InitializeImmutableBinding(uniqueName, initializer);
            binding.IsGenerated = true;

            // make sure we set the crunchability of this field to TRUE. Doesn't matter
            // whether it's a global or within a with-scope or what-have-you. It didn't
            // exist in the sources (we are generating it now) so we can rename it whatever
            // the heck we want.
            binding.CanRename = true;

            VariableDeclaration varDecl = new VariableDeclaration(
                null,
                Parser,
                uniqueName,
                new Context(Parser),
                initializer);

            if (varStatement != null)
            {
                // the first statement is a var; just add a new declaration to the front of it
                varStatement.InsertAt(0, varDecl);
            }
            else
            {
                // create a new var statement
                varStatement = new VarStatement(null, Parser);
                varStatement.Append(varDecl);

                // if the first statement is a for with an EMPTY initializer, stuff the var
                // in there. Otherwise stick it before the first statement.
                if (forStatement != null && forStatement.Initializer == null)
                {
                    forStatement.Initializer = varStatement;
                }
                else
                {
                    Body.Insert(ndxFirst, varStatement);
                }
            }

            return binding;
        }
    }
}