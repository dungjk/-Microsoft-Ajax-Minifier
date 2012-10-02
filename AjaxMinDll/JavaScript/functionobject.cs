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

namespace Microsoft.Ajax.Utilities
{
    public sealed class FunctionObject : AstNode, INameDeclaration
    {
        public Block Body { get; private set; }
        public AstNodeList ParameterDeclarations { get; private set; }

        public FunctionType FunctionType { get; private set; }

        public bool HasInitializer { get { return false; } }

        public Context NameContext { get { return IdContext; } }

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

        public Lookup Identifier { get; private set; }
        private string m_name;
        public string Name
        {
            get
            {
                return (Identifier != null ? Identifier.Name : m_name);
            }
            set
            {
                if (Identifier != null)
                {
                    Identifier.Name = value;
                }
                else
                {
                    m_name = value;
                }
            }
        }
        public Context IdContext { get { return (Identifier == null ? null : Identifier.Context); } }

        public override bool IsExpression
        {
            get
            {
                // if this is a declaration, then it's not an expression. Otherwise treat it 
                // as if it were an expression.
                return !(FunctionType == FunctionType.Declaration);
            }
        }

        // when parsed, this flag indicates that a function declaration is in the
        // proper source-element location
        public bool IsSourceElement
        {
            get;
            set;
        }

        public JSVariableField VariableField { get; set; }
        public int RefCount { get { return (VariableField == null ? 0 : VariableField.RefCount); } }

        public FunctionScope FunctionScope { get; set; }

        public override ActivationObject EnclosingScope
        {
            get
            {
                return FunctionScope;
            }
        }

        public override OperatorPrecedence Precedence
        {
            get
            {
                // just assume primary -- should only get called for expressions anyway
                return OperatorPrecedence.Primary;
            }
        }

        public FunctionObject(Lookup identifier, JSParser parser, FunctionType functionType, AstNodeList parameterDeclarations, Block bodyBlock, Context functionContext)
            : base(functionContext, parser)
        {
            FunctionType = functionType;
            m_name = string.Empty;
            Identifier = identifier;
            if (Identifier != null) { Identifier.Parent = this; }

            ParameterDeclarations = parameterDeclarations;

            Body = bodyBlock;
            if (bodyBlock != null) { bodyBlock.Parent = this; }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public bool IsReferenced
        {
            get
            {
                // call the checking method with a new empty hashset so it doesn't
                // go in an endless circle
                return SafeIsReferenced(new HashSet<FunctionObject>());
            }
        }

        private bool SafeIsReferenced(HashSet<FunctionObject> visited)
        {
            // if we've already been here, don't go in a circle
            if (!visited.Contains(this))
            {
                // add us to the visited list
                visited.Add(this);

                if (FunctionType == FunctionType.Declaration)
                {
                    // this is a function declaration, so it better have it's variable field set.
                    // if the variable (and therefore the function) is defined in the global scope,
                    // then this function declaration is called by a global function and therefore is
                    // referenced.
                    if (VariableField.OwningScope is GlobalScope)
                    {
                        return true;
                    }

                    // not defined in the global scope. Check its references.
                    foreach (var reference in VariableField.References)
                    {
                        var referencingScope = reference.VariableScope;
                        if (referencingScope is GlobalScope)
                        {
                            // referenced by a lookup in the global scope -- we're good to go.
                            return true;
                        }
                        else
                        {
                            var functionScope = referencingScope as FunctionScope;
                            if (functionScope != null && functionScope.FunctionObject.SafeIsReferenced(visited))
                            {
                                // as soon as we find one that's referenced, we stop
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    // expressions are always referenced
                    return true;
                }
            }

            // if we get here, we aren't referenced by anything that's referenced
            return false;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(ParameterDeclarations, Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Body == oldNode)
            {
                if (newNode == null)
                {
                    // just remove it
                    Body = null;
                    return true;
                }
                else
                {
                    // if the new node isn't a block, ignore it
                    Block newBlock = newNode as Block;
                    if (newBlock != null)
                    {
                        Body = newBlock;
                        newNode.Parent = this;
                        return true;
                    }
                }
            }
            else if (ParameterDeclarations == oldNode)
            {
                if (newNode == null)
                {
                    // just remove it
                    ParameterDeclarations = null;
                    return true;
                }
                else
                {
                    // if the new node isn't a block, ignore it
                    var newList = newNode as AstNodeList;
                    if (newList != null)
                    {
                        ParameterDeclarations = newList;
                        newList.Parent = this;
                        return true;
                    }
                }
            }
            return false;
        }

        internal override bool RequiresSeparator
        {
            get { return HideFromOutput; }
        }

        internal bool IsArgumentTrimmable(JSVariableField targetArgumentField)
        {
            // walk backward until we either find the given argument field or the
            // first parameter that is referenced. 
            // If we find the argument field, then we can trim it because there are no
            // referenced parameters after it.
            // if we find a referenced argument, then the parameter is not trimmable.
            JSVariableField argumentField = null;
            if (ParameterDeclarations != null)
            {
                for (int index = ParameterDeclarations.Count - 1; index >= 0; --index)
                {
                    // better be a parameter declaration
                    argumentField = (ParameterDeclarations[index] as ParameterDeclaration).IfNotNull(p => p.VariableField);
                    if (argumentField != null
                        && (argumentField == targetArgumentField || argumentField.IsReferenced))
                    {
                        break;
                    }
                }
            }

            // if the argument field we landed on is the same as the target argument field,
            // then we found the target argument BEFORE we found a referenced parameter. Therefore
            // the argument can be trimmed.
            return (argumentField == targetArgumentField);
        }
    }
}