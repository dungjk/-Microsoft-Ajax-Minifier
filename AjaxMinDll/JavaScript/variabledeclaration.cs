// variabledeclaration.cs
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

using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public sealed class VariableDeclaration : AstNode, INameDeclaration, INameReference
    {
        public string Identifier { get; private set; }
        public Context IdentifierContext { get; private set; }
        public Context NameContext { get { return IdentifierContext; } }

        public AstNode Initializer { get; private set; }
        public bool HasInitializer { get { return Initializer != null; } }

        public JSVariableField VariableField { get; set; }
        public bool IsCCSpecialCase { get; set; }
        public bool UseCCOn { get; set; }

        public string Name
        {
            get { return Identifier; }
        }

        public bool RenameNotAllowed
        {
            get
            {
                return VariableField == null ? true : !VariableField.CanCrunch;
            }
        }

        private bool m_isGenerated;
        public bool IsGenerated
        {
            get { return m_isGenerated; }
            set
            {
                m_isGenerated = value;
                if (VariableField != null)
                {
                    VariableField.IsGenerated = m_isGenerated;
                }
            }
        }

        public VariableDeclaration(Context context, JSParser parser, string identifier, Context idContext, AstNode initializer)
            : base(context, parser)
        {
            // identifier cannot be null
            Identifier = identifier;
            IdentifierContext = idContext;

            // initializer may be null
            Initializer = initializer;
            if (Initializer != null) { Initializer.Parent = this; }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override bool IsExpression
        {
            get
            {
                // sure. treat a vardecl like an expression. normally this wouldn't be anywhere but
                // in a var statement, but sometimes the special-cc case might be moved into an expression
                // statement
                return true;
            }
        }

        internal override string GetFunctionGuess(AstNode target)
        {
            return Identifier;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Initializer);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Initializer == oldNode)
            {
                Initializer = newNode;
                if (newNode != null) { newNode.Parent = this; }
                return true;
            }
            return false;
        }

        public override bool IsEquivalentTo(AstNode otherNode)
        {
            JSVariableField otherField = null;
            Lookup otherLookup;
            var otherVarDecl = otherNode as VariableDeclaration;
            if (otherVarDecl != null)
            {
                otherField = otherVarDecl.VariableField;
            }
            else if ((otherLookup = otherNode as Lookup) != null)
            {
                otherField = otherLookup.VariableField;
            }

            // if we get here, we're not equivalent
            return this.VariableField != null && this.VariableField.IsSameField(otherField);
        }

        #region INameReference Members

        public ActivationObject VariableScope
        {
            get
            {
                // if we don't have a field, return null. Otherwise it's the field's owning scope.
                return this.VariableField.IfNotNull(f => f.OwningScope);
            }
        }

        #endregion
    }
}
