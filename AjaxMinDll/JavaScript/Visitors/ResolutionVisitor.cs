// ResolutionVisitor.cs
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
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript.Visitors
{
    public class ResolutionVisitor : TreeVisitor
    {
        // we might be creating bindings for NFEs and such, so we need both stacks
        private Stack<LexicalEnvironment> m_lexicalStack;
        private Stack<LexicalEnvironment> m_variableStack;

        private ObjectEnvironment m_globalEnvironment;
        private CodeSettings m_codeSettings;

        public static void Resolve(Block program, ObjectEnvironment global, CodeSettings settings)
        {
            if (program != null)
            {
                // if we weren't passed a global scope, create one now -- just in case
                global = global ?? new ObjectEnvironment(null, typeof(GlobalObject));

                // identify the global directive prologues (if any) and see if one of them is the strict mode directive
                if (IdentifyDirectivePrologues(program))
                {
                    global.UseStrict = true;
                }

                // initialize the global scope with the program declarations
                // (not a function, so pass false) and start the positions at zero.
                InitializeDeclarations(global, program, global.UseStrict, false, 0, settings == null ? true : !settings.PreserveFunctionNames);

                // the program block has the global scope as both the lexical and variable environments
                program.LexicalEnvironment = program.VariableEnvironment = global;

                // now visit the program to kick off the process
                var visitor = new ResolutionVisitor(global, settings);
                program.Accept(visitor);
            }
        }

        private ResolutionVisitor(ObjectEnvironment global, CodeSettings codeSettings)
        {
            // save the code settings
            m_codeSettings = codeSettings ?? new CodeSettings();

            // start off with the global context
            m_lexicalStack = new Stack<LexicalEnvironment>();
            m_variableStack = new Stack<LexicalEnvironment>();

            // and save the global
            m_globalEnvironment = global;
        }

        private static bool IdentifyDirectivePrologues(Block node)
        {
            var isStrict = false;
            if (node != null)
            {
                // directive prologues are zero or more expression statements consisting of a single 
                // string literal at the very beginning of the Program or Function block. So walk forward
                // from the first node in the block, and as long as we find the node is a ConstantWrapper
                // that is a string literal, keep on looking. As soon as we find the first node that doesn't fit
                // that description, we are done.
                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    var constantWrapper = node[ndx] as ConstantWrapper;
                    if (constantWrapper != null
                        && constantWrapper.PrimitiveType == PrimitiveType.String)
                    {
                        // yup -- found one
                        constantWrapper.IsDirectivePrologue = true;

                        // see if it's the "use strict" directive
                        if (string.CompareOrdinal(constantWrapper.ToString(), "use strict") == 0)
                        {
                            // it might be -- it can't have any escapes or anything, which the ToString method
                            // will hide. If it has a context, check that to make doubly-sure. If it doesn't have
                            // a context, assume it was added and is okay.
                            if (Object.ReferenceEquals(constantWrapper.Context, null) ||
                                string.CompareOrdinal(constantWrapper.Context.Code, 1, "use strict", 0, 10) == 0)
                            {
                                // set strict mode for our current scope
                                isStrict = true;
                            }
                        }
                    }
                    else
                    {
                        // nope -- so we're done; break out of the loop
                        break;
                    }
                }
            }

            return isStrict;
        }

        private static void InitializeDeclarations(LexicalEnvironment lexicalEnvironment, Block block, bool isStrict, bool isFunction, int position, bool canRename)
        {
            // walk the function block and collect all the variable and function declarations
            var declarationVisitor = DeclarationVisitor.Apply(block);

            // add the function declarations
            if (declarationVisitor.Functions != null)
            {
                foreach (var function in declarationVisitor.Functions)
                {
                    // it will only ever be a named declaration or an expression
                    if (function.FunctionType == FunctionType.Declaration)
                    {
                        // if we marked this function as having an error in the name, don't bother
                        // trying to resolve it. We might try to fix it later; we might not and just
                        // output what came in
                        if (!function.NameError)
                        {
                            Binding binding;
                            if (lexicalEnvironment.TryGetBinding(function.Name, out binding))
                            {
                                // already exists. See what kind it is.
                                if (binding.Category == BindingCategory.NamedFunctionExpression)
                                {
                                    // this is actually OK. We've encountered a named function expression
                                    // binding, which are really only [incorrectly] seen by IE -- BUT a
                                    // function declaration has come along after it! 
                                    // So in non-IE browsers, it doesn't matter -- they always see the declaration
                                    // in this scope. And for IE, the declaration (because it's coming _after_)
                                    // overrides the NFE -- so all is well in cross-browser world.
                                    // update the category of this binding to be normal
                                    binding.Category = BindingCategory.Normal;
                                }
                                else if (binding.Category == BindingCategory.Predefined)
                                {
                                    // must be the global environment, but we're trying to declare a function.
                                    // the declared function takes precedence, so create a new binding.
                                    binding = lexicalEnvironment.CreateMutableBinding(function.Name, false);
                                    binding.Position = position++;
                                    binding.Category = BindingCategory.Normal;
                                }
                                else
                                {
                                    // throw a warning about the name being duplicated.
                                    // at this point it's because either another function declaration or
                                    // an argument has the same name.
                                    // if we're initializing the global scope, it might contain variables from
                                    // a previous program block. But those are okay, too. 
                                    function.IdContext.HandleError(JSError.DuplicateName, false);

                                    // make sure we set the category to normal because if the existing binding is
                                    // for an argument, we've just made the argument unreachable.
                                    binding.Category = BindingCategory.Normal;
                                }

                                // use this binding
                                function.Binding = binding;
                                binding.DefinitionContext = function.IdContext;
                            }
                            else
                            {
                                // doesn't exist -- create a new binding and use it
                                function.Binding = lexicalEnvironment.CreateMutableBinding(function.Name, false);
                                function.Binding.Position = position++;
                                function.Binding.DefinitionContext = function.IdContext;
                            }

                            // the value of the binding is the function object itself
                            function.Binding.Value = function;
                        }
                    }
                    else //if (function.FunctionType == FunctionType.Expression)
                    {
                        // need to treat these special because only IE specifies the name of the
                        // named function expressions in the _containing_ scope and we want to be able to
                        // notify the developer when a potential cross-browser issue with their named
                        // function expression may exist.
                        // DON'T actually set the function's binding here -- we'll do that later and point
                        // it to the REAL binding that we'll create in a new environment at that time.
                        Binding binding;
                        if (lexicalEnvironment.TryGetBinding(function.Name, out binding))
                        {
                            // it already exists -- we might have a problem!
                            // it must be either a function declaration, another NFE, or an argument,
                            // since we haven't gotten around to creating the variables yet. 
                            if (binding.Category == BindingCategory.NamedFunctionExpression)
                            {
                                // it's another NFE with the same name. Not a problem in non-IE browsers.
                                // just set the value to be the latest NFE, since this one will take precedence.
                                binding.Value = function;
                            }
                            else
                            {
                                // BindingCategory.Normal:
                                // can only be a function declaration, since we haven't added the variables yet.
                                // this will be a problem because IE will expose the function expression as
                                // the object with this name, not the function declaration. But all other browsers
                                // will see the function declaration. BUT, if the name is never referenced in this 
                                // scope, we won't care anyway.
                                // set the ambiguous value to be the NFE, since it would only have this value
                                // in IE browsers.

                                // BindingCategory.Argument:
                                // this one will be a problem IF the argument is ever referenced outside the NFE. 
                                // IE will think it's the NFE while all other browsers will think it's the argument 
                                // passed in.
                                // set the ambiguous value to be the NFE, since it would only have this value
                                // in IE browsers.

                                // BindingCategory.Predefined:
                                // this can happen if the function expression is in the global scope and has the same
                                // name as a predefined global object property. This is no problem for non-IE browsers,
                                // but can cause problems in IE if we try to access the name and expect the global
                                // object property -- we'll get the NFE instead. 

                                // shouldn't be any other values, but in case -- just set the ambiguous value to this function
                                binding.AmbiguousValue = function;
                            }
                        }
                        else
                        {
                            // doesn't exist yet -- create it now
                            binding = lexicalEnvironment.CreateMutableBinding(function.Name, false);

                            // use the NFE category, which means this isn't a _real_ field in the environment,
                            // it's just a placeholder so we know whether to keep it in sync with another field,
                            // or as a placeholder to make sure we don't accidentally rename our function expression
                            // to something else that exists in this scope. 
                            binding.Category = BindingCategory.NamedFunctionExpression;
                            binding.DefinitionContext = function.IdContext;

                            // if we have the preserve-function-names option set, we don't want to rename these fields
                            // (don't just set to the logical-not of the setting because we don't want to change
                            // it to renameable if it's not)
                            if (!canRename)
                            {
                                binding.CanRename = false;
                            }

                            // the value of the binding is the function object
                            binding.Value = function;
                        }
                    }
                }
            }

            // add "arguments" if this is a function and it isn't already present
            if (isFunction && !lexicalEnvironment.HasBinding("arguments"))
            {
                Binding argumentsBinding;
                if (isStrict)
                {
                    // if this is strict mode, it's an immutable binding
                    // and because this is for a function, we know it's a declarative environment
                    var declarativeEnvironment = (DeclarativeEnvironment)lexicalEnvironment;
                    argumentsBinding = declarativeEnvironment.CreateImmutableBinding("arguments");
                    declarativeEnvironment.InitializeImmutableBinding("arguments", null);
                }
                else
                {
                    // otherwise it's just a normal mutable binding
                    argumentsBinding = lexicalEnvironment.CreateMutableBinding("arguments", false);
                }

                // set the type to be the special arguments object and specify that it cannot be renamed
                argumentsBinding.Category = BindingCategory.Arguments;
                argumentsBinding.CanRename = false;
            }

            // add variable declarations
            if (declarationVisitor.VariableDeclarations != null)
            {
                foreach (var varDecl in declarationVisitor.VariableDeclarations)
                {
                    // if we haven't created the field yet....
                    Binding binding;
                    if (!lexicalEnvironment.TryGetBinding(varDecl.Identifier, out binding))
                    {
                        // create the binding and set the position
                        binding = lexicalEnvironment.CreateMutableBinding(varDecl.Identifier, false);
                        binding.Position = position++;
                        binding.DefinitionContext = varDecl.IdentifierContext;
                    }
                    else
                    {
                        // if the existing binding is an NFE, then this could create cross-browser issues
                        // IF the var isn't being initialized to the NFE itself (common way to get around the
                        // issue).
                        if (binding.Category == BindingCategory.NamedFunctionExpression)
                        {
                            if (varDecl.Initializer == binding.Value)
                            {
                                // if the variable is being initialized to the NFE itself, then this isn't a problem.
                                // go ahead and set its status to normal; nothing ambiguous here
                                binding.Category = BindingCategory.Normal;
                            }
                            else
                            {
                                // no initializer at all, or it will be initialized to something else when
                                // the var-decl is executed. this will only be a problem if the variable is referenced,
                                // because in IE it will be the NFE, but non-IE will be undefined
                                // set the value to null and the NFE to the ambiguous value
                                binding.AmbiguousValue = binding.Value;
                                binding.Category = BindingCategory.Normal;
                                binding.DefinitionContext = varDecl.IdentifierContext;

                                // TODO: and engine would set it to undefined until the var-decl is executed.
                                // should we be setting it to the initializer for analysis purposes?
                                binding.Value = varDecl.Initializer;
                            }
                        }
                        else if (varDecl.Initializer != null)
                        {
                            // throw warning about duplicated name -- either another var, a function declaration,
                            // or an argument.
                            varDecl.State = VarDeclarationState.AlreadyDefined;
                            varDecl.IdentifierContext.HandleError(JSError.DuplicateName, false);
                        }
                        else
                        {
                            // if the varDecl initialier is null, then the var-decl does absolutely nothing!
                            // the field already exists, it doesn't set it to anything -- meaningless!
                            // warn about a superfluous var-decl
                            varDecl.State = VarDeclarationState.Superfluous;
                            varDecl.IdentifierContext.HandleError(JSError.SuperfluousVarDeclaration, false);
                        }
                    }

                    // set the binding on the vardecl
                    varDecl.Binding = binding;
                }
            }
        }

        public override void Visit(Block node)
        {
            if (node != null)
            {
                try
                {
                    // if there's an execution context for this block, push it on
                    // the stack now before we recurse
                    // (yes, this means we will have the global scope on the stack twice; big deal)
                    if (node.LexicalEnvironment != null)
                    {
                        m_lexicalStack.Push(node.LexicalEnvironment);
                    }
                    if (node.VariableEnvironment != null)
                    {
                        m_variableStack.Push(node.VariableEnvironment);
                    }

                    base.Visit(node);
                }
                finally
                {
                    if (node.LexicalEnvironment != null)
                    {
                        m_lexicalStack.Pop();
                    }
                    if (node.VariableEnvironment != null)
                    {
                        m_variableStack.Pop();
                    }
                }
            }
        }

        public override void Visit(FunctionObject node)
        {
            if (node != null)
            {
                LexicalEnvironment current = m_lexicalStack.Peek();

                // let's try identifying directive prologues now before we set up any other lexical
                // environment(s) so we can get the strict-mode setting in play (if any)
                var isStrict = IdentifyDirectivePrologues(node.Body) || current.UseStrict;

                // if this is a named function expression, we're going to create TWO lexical
                // environments. the first one will have just the name of the function as an
                // immutable field, and that will be the parent of the normal function object's
                // lexical environment
                DeclarativeEnvironment functionExpressionEnvironment = null;
                if (node.FunctionType == FunctionType.Expression && !string.IsNullOrEmpty(node.Name))
                {
                    // create a declarative environment that is a child of the current environment
                    functionExpressionEnvironment = current.NewDeclarativeEnvironment();
                    functionExpressionEnvironment.UseStrict = isStrict;

                    // set the function expression name as the immutable binding in this new environment,
                    // with a value of the FunctionObject
                    node.Binding = functionExpressionEnvironment.CreateImmutableBinding(node.Name);
                    functionExpressionEnvironment.InitializeImmutableBinding(node.Name, node);

                    // now, here is where it gets tricky.
                    // IE (less than 9) creates the name in the _parent_ scope, which is different than the
                    // language specs as implemented by almost all other browsers. We should have already created 
                    // a placeholder for it (category = nfe) when we set up the parent function environment, 
                    // so just try getting it now on teh variable environment.
                    LexicalEnvironment variableEnvironment = m_variableStack.Peek();
                    Binding nfeBinding;
                    if (variableEnvironment.TryGetBinding(node.Name, out nfeBinding))
                    {
                        // link our binding to the NFE binding to keep them in sync. We want to make sure
                        // that if we rename the function expression, it stays the exact same name as the variable.
                        // We don't HAVE to, but if we rename the function expression to something else, and that 
                        // something else already exists in the parent scope, then we've CREATED a conflict for IE
                        // browsers where none may exist as-is. Just make sure the names stay the same. 
                        // If the outer scope is the global environment, than means the outer variable won't be
                        // renamed -- and that's fine. We _could_ rename_ the function expression, but to what?
                        // how would we know the new name wouldn't collide with *something* out there in the global
                        // environment. It will just have to stay the same name. 
                        node.Binding.Linked = new Reference(node.Name, variableEnvironment, node.IdContext);
                    }
                    else
                    {
                        // no NFE binding? this shouldn't happen. It should've been created when
                        // we set up the variable environment earlier. Hmmmm....
                    }
                }

                // create the new lexical environment chained to either the current environment
                // of the function expression environment if present
                var lexicalEnvironment = (functionExpressionEnvironment ?? current).NewDeclarativeEnvironment();
                lexicalEnvironment.UseStrict = isStrict;

                // set the flag that tags this scope as being a function scope
                lexicalEnvironment.IsFunctionScope = true;

                // add the argument names
                var position = 0;
                if (node.ParameterDeclarations != null && node.ParameterDeclarations.Count > 0)
                {
                    foreach (var parameter in node.ParameterDeclarations)
                    {
                        Binding binding;
                        if (lexicalEnvironment.TryGetBinding(parameter.Name, out binding))
                        {
                            // already defined -- duplicate parameter variables.
                            // point this parameter to the same binding object, but don't update its position
                            parameter.Binding = binding;

                            if (isStrict)
                            {
                                // strict-mode is an error when dup parameters are encountered
                                parameter.Context.HandleError(JSError.StrictModeDuplicateArgument, true);
                            }
                            else
                            {
                                // non-strict mode is just a warning
                                parameter.Context.HandleError(JSError.DuplicateName, false);
                            }
                        }
                        else
                        {
                            // wasn't defined -- define it now
                            // and set the binding on the parameter declaration object
                            parameter.Binding = lexicalEnvironment.CreateMutableBinding(parameter.Name, false);
                            parameter.Binding.Category = BindingCategory.Argument;
                            parameter.Binding.Position = position++;
                            parameter.Binding.DefinitionContext = parameter.Context;
                        }
                    }
                }

                // initialize the function scope's declarations
                InitializeDeclarations(lexicalEnvironment, node.Body, isStrict, true, position, !m_codeSettings.PreserveFunctionNames);

                // set the function's lexical environment to this new env
                node.LexicalEnvironment = lexicalEnvironment;
                
                try
                {
                    // push it on the stack and recurse
                    m_lexicalStack.Push(lexicalEnvironment);
                    m_variableStack.Push(lexicalEnvironment);

                    base.Visit(node);

                    // we've finished the lexical references for this scope and all its children.
                    // now go through our ambiguous bindings. If there were no references
                    // to our ambiguous binding, then all is good. If there were no references to
                    // the function expression's binding (which means nothing inside it referenced the
                    // name, then that is also good. Throw an error, however, if both scopes referenced
                    // the name.
                    if (lexicalEnvironment.DefinedWithin != null)
                    {
                        foreach (var binding in lexicalEnvironment.DefinedWithin)
                        {
                            var funcObj = binding.AmbiguousValue as FunctionObject;
                            if (funcObj != null)
                            {
                                // we have a binding we had flagged earlier as a potential ambiguous
                                // circumstance between IE and non-IE. However, if we have no references
                                // on our end, everything is good.
                                if (binding.ReferenceCount != 0)
                                {
                                    // we have references.
                                    // well, if the code within the function expression never references
                                    // the name, then we're still good.
                                    if (funcObj.Binding != null && funcObj.Binding.ReferenceCount > 0)
                                    {
                                        // okay, both environments reference the same name. For non-IE
                                        // browsers, this is just fine because they are separate bindings.
                                        // but for IE<9, it's the same binding, which means wehave a 
                                        // potential cross-browser situation here: for IE<9, the value of the 
                                        // binding might not be as expected in either scope. 
                                        // Fire an error on the function expression.
                                        funcObj.IdContext.HandleError(JSError.AmbiguousNamedFunctionExpression, false);
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // pop 'em off
                    m_lexicalStack.Pop();
                    m_variableStack.Pop();
                }

                // finished resolving this function and everything underneath it.
                // throw errors/warnings for things I've declared that aren't referenced.
                // ignore arguments in this loop -- we'll treat them special afterwards
                var definedWithin = lexicalEnvironment.DefinedWithin;
                if (definedWithin != null)
                {
                    foreach (var binding in definedWithin)
                    {
                        if (binding.ReferenceCount == 0)
                        {
                            if (binding.Category == BindingCategory.Normal)
                            {
                                FunctionObject funcObj;
                                if ((funcObj = binding.Value as FunctionObject) != null)
                                {
                                    if (funcObj.FunctionType == FunctionType.Declaration)
                                    {
                                        // unreferenced function declaration
                                        (binding.DefinitionContext ?? funcObj.IdContext).HandleError(
                                            JSError.FunctionNotReferenced,
                                            false);
                                    }
                                }
                                else
                                {
                                    // unreferenced variable
                                    binding.DefinitionContext.HandleError(JSError.VariableDefinedNotReferenced, false);
                                }
                            }
                        }
                    }
                }
                
                // unreferenced arguments can only be removed if they are the last argument, or if no
                // other arguments after them are referenced, either. So let's walk backwards through the 
                // arguments -- as soon as we hit a referenced argument, we are done.
                if (node.ParameterDeclarations != null)
                {
                    for (var ndx = node.ParameterDeclarations.Count - 1; ndx >= 0; --ndx)
                    {
                        var parameterDeclaration = node.ParameterDeclarations[ndx];
                        var binding = parameterDeclaration.Binding;

                        if (binding.Category != BindingCategory.Argument)
                        {
                            // if the category is no longer argument, then this argument was
                            // hidden by a function declaration and might as well be unreferenced.
                            parameterDeclaration.Context.HandleError(JSError.HiddenArgument, false);
                        }
                        else if (binding.ReferenceCount == 0)
                        {
                            // unreferenced argument
                            parameterDeclaration.Context.HandleError(JSError.ArgumentNotReferenced, false);
                        }
                        else
                        {
                            // otherwise we are done
                            break;
                        }
                    }
                }
            }
        }

        public override void Visit(Lookup node)
        {
            if (node != null)
            {
                // figure out if our reference type is a Function or a Constructor
                // (the default was initialized to Variable)
                var parentCall = node.Parent as CallNode;
                if (parentCall != null)
                {
                    node.RefType = parentCall.IsConstructor
                        ? ReferenceType.Constructor
                        : ReferenceType.Function;
                }

                // by the time we recurse into the bodies and start resolving lookups,
                // the lexical stack has already been put in place, so the references should
                // get resolved just fine.
                LexicalEnvironment current = m_lexicalStack.Peek();
                current.ResolveLookup(node);

                // the Base field will be null if the reference is undefined
                if (node.Reference.Base == null)
                {
                    // undefined reference
                    // TODO: need to FIRST check to see if this is our resource-string object!

                    // create the field in the global scope. by default the type will be normal.
                    // we might change it to undefined later
                    var binding = m_globalEnvironment.CreateMutableBinding(node.Name, true);

                    // set the definition context to be the first instance -- that's the point at
                    // which the window-object property is created.
                    binding.DefinitionContext = node.Context;

                    // and point the reference to the global scope
                    node.Reference.Base = m_globalEnvironment;

                    // if it's one of our "known globals," then it's good to go
                    if (m_codeSettings != null
                        && (m_codeSettings.KnownGlobalNames == null
                        || !m_codeSettings.KnownGlobalNames.Contains(node.Name)))
                    {
                        // if it's the operand of a typeof operator, it's okay
                        if (!(node.Parent is TypeOfNode))
                        {
                            // if it's the left-hand side of an assign, then it's okay
                            var binOp = node.Parent as BinaryOperator;
                            if (binOp == null 
                                || binOp.OperatorToken != JSToken.Assign
                                || node != binOp.Operand1)
                            {
                                // report this undefined reference
                                binding.Category = BindingCategory.Undefined;
                                node.Context.ReportUndefined(node);

                                // and throw a possibly-undefined-global warning. If this is the function of a call node,
                                // then report it as an undefined function; otherwise assume it's a variable
                                node.Context.HandleError(
                                  (parentCall != null && parentCall.Function == node ? JSError.UndeclaredFunction : JSError.UndeclaredVariable),
                                  false);
                            }
                        }
                    }
                }
                //else
                //{
                //    // the reference already exists
                //    // get the binding
                //    Binding binding;
                //    if (node.Reference.Base.TryGetBinding(node.Name, out binding)
                //        && binding.AmbiguousValue != null)
                //    {
                //        // it's ambiguous. BUT if we are _assigning_ to this lookup, then it's 
                //        // okay because we don't care what the value WAS -- we're changing it
                //        var binOp = node.Parent as BinaryOperator;
                //        if (binOp == null || binOp.OperatorToken != JSToken.Assign
                //            || binOp.Operand1 != node)
                //        {
                //            // not assigning, which means we _will_ be reading this value, and it's
                //            // cross-browser ambiguous. Throw a warning.
                //            node.Context.HandleError(JSError.AmbiguousNamedFunctionExpression, false);
                //        }
                //    }
                //}
            }
        }

        public override void Visit(TryStatement node)
        {
            if (node != null)
            {
                if (node.CatchBlock != null)
                {
                    LexicalEnvironment current = m_lexicalStack.Peek();

                    // create the execution context and set it up with the catch var name
                    LexicalEnvironment catchLex = current.NewDeclarativeEnvironment();
                    var binding = catchLex.CreateMutableBinding(node.CatchVarName, false);
                    binding.DefinitionContext = node.CatchVarContext;
                    binding.Category = BindingCategory.CatchArgument;
                    node.CatchBinding = binding;

                    // set the block's lexical environment -- when we recurse, it will get pushed on and popped
                    // off and the right times
                    node.CatchBlock.LexicalEnvironment = catchLex;

                    // see if the outer environment has a field with the same name
                    // if so, we want to link the catch variable to it so they keep the
                    // same names should we later rename fields
                    if (current.HasBinding(node.CatchVarName))
                    {
                        binding.Linked = current.GetIdentifierReference(node.CatchVarName, node.CatchVarContext);
                    }
                    else
                    {
                        // it doesn't -- there are no potential IE name collisions between our catch variable and 
                        // a same-named variable in the outer scope. We need to KEEP it that way, so let's create a
                        // phantom placeholder binding in the current (parent of the catch) environment that will get
                        // named so it doesn't collide with anything else, and the catch var will be linked to it.
                        var phantomBinding = current.CreatePlaceholder(node.CatchVarContext);
                        phantomBinding.DefinitionContext = node.CatchVarContext;

                        // link them together so the catch variable will get the same name as the phantom binding, which
                        // will be guaranteed later not to collide with anything in the current scope
                        binding.Linked = current.GetIdentifierReference(phantomBinding.Name, node.CatchVarContext);
                    }
                }

                // recurse
                base.Visit(node);
            }
        }

        public override void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                // recurse -- will resolve anything in the initializer (if there is one)
                base.Visit(node);

                if (node.Initializer != null)
                {
                    // we're going to be initializing this variable at this time; the binding
                    // was created when the scope was entered and the value left undefined.
                    // so *really* this is a kind of a reference. 
                    // let's get a reference to this name and see if it resolves to our current
                    // object-environment scope that isn't the global environment. That would mean
                    // we are referring to the current with-scope.
                    var current = m_lexicalStack.Peek();
                    var reference = current.GetIdentifierReference(node.Identifier, node.IdentifierContext);

                    ObjectEnvironment objectEnv = null;
                    if ((objectEnv = reference.Base as ObjectEnvironment) != null
                        && !objectEnv.IsGlobal
                        && objectEnv == m_lexicalStack.Peek())
                    {
                        // the reference is to the current scope, which is a with-environment.
                        // the call to GetIdentifierReference merely gets a reference to the binding;
                        // it doesn't actually create it. But wewant future references to hit a
                        // property binding in this scope, so get the binding associated with this
                        // reference. It will create the property binding in our scope and mark the
                        // outer reference as referenced.
                        var binding = reference.Binding;
                        binding.CanRename = binding.CanRename;
                    }
                }
            }
        }

        public override void Visit(WithStatement node)
        {
            if (node != null)
            {
                LexicalEnvironment current = m_lexicalStack.Peek();

                // create the execution context and set it up with the catch var name
                ObjectEnvironment withLex = current.NewObjectEnvironment(null);

                // set the block's execution context -- when we recurse, it will get pushed on and popped
                // off and the right times
                node.Body.LexicalEnvironment = withLex;

                // recurse
                base.Visit(node);
            }
        }
    }
}
