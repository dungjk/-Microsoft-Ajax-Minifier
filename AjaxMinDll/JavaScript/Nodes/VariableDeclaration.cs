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

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public enum VarDeclarationState
    {
        Normal = 0,
        AlreadyDefined,
        Superfluous
    }

    public sealed class VariableDeclaration : AstNode
    {
        public VarDeclarationState State { get; set; }

        public string Identifier { get; private set; }
        public Context IdentifierContext { get; private set; }

        private AstNode m_initializer;
        public AstNode Initializer
        {
            get { return m_initializer; }
            set
            {
                if (value != m_initializer)
                {
                    if (m_initializer != null && m_initializer.Parent == this)
                    {
                        m_initializer.Parent = null;
                    }
                    m_initializer = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public bool IsCCSpecialCase { get; set; }
        public bool UseCCOn { get; set; }

        private Binding m_binding;
        public Binding Binding 
        {
            get
            {
                if (m_binding == null && Parent != null)
                {
                    // we don't have a binding set, but we are asking for it.
                    // see if we can get that created now
                    var variableEnvironment = Parent.EnclosingVariableEnvironment;
                    if (variableEnvironment != null)
                    {
                        // if it doesn't exist already...
                        if (!variableEnvironment.TryGetBinding(Identifier, out m_binding))
                        {
                            // create the binding now
                            m_binding = variableEnvironment.CreateMutableBinding(Identifier, false);
                        }
                        //else
                        //{
                        //    // already exists... check for anything?
                        //}
                    }
                }
                return m_binding;
            }
            set { m_binding = value; }
        }

        public VariableDeclaration(Context context, JSParser parser, string identifier, Context idContext, AstNode initializer)
            : base(context, parser)
        {
            // identifier cannot be null
            Identifier = identifier;
            IdentifierContext = idContext;

            // initializer may be null
            Initializer = initializer;
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

        public override string GetFunctionNameGuess(AstNode target)
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
                return true;
            }
            return false;
        }

        public override bool IsEquivalentTo(AstNode otherNode)
        {
            Binding otherBinding = null;
            Lookup otherLookup;
            var otherVarDecl = otherNode as VariableDeclaration;
            if (otherVarDecl != null)
            {
                otherBinding = otherVarDecl.Binding;
            }
            else if ((otherLookup = otherNode as Lookup) != null)
            {
                otherBinding = otherLookup.Binding;
            }

            return this.Binding != null && this.Binding == otherBinding;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();

        //    // append the name of the field -- use the binding if available
        //    sb.Append(Binding != null ? Binding.ToString() : Identifier);
        //    if (Initializer != null)
        //    {
        //        if (IsCCSpecialCase)
        //        {
        //            sb.Append(UseCCOn ? "/*@cc_on=" : "/*@=");
        //        }
        //        else
        //        {

        //            if (Parser.Settings.OutputMode == OutputMode.MultipleLines && Parser.Settings.IndentSize > 0)
        //            {
        //                sb.Append(" = ");
        //            }
        //            else
        //            {
        //                sb.Append('=');
        //            }
        //        }

        //        bool useParen = false;
        //        // a comma operator is the only thing with a lesser precedence than an assignment
        //        BinaryOperator binOp = Initializer as BinaryOperator;
        //        if (binOp != null && binOp.OperatorToken == JSToken.Comma)
        //        {
        //            useParen = true;
        //        }
        //        if (useParen)
        //        {
        //            sb.Append('(');
        //        }
        //        sb.Append(Initializer.ToCode(IsCCSpecialCase ? ToCodeFormat.Preprocessor : ToCodeFormat.Normal));
        //        if (useParen)
        //        {
        //            sb.Append(')');
        //        }

        //        if (IsCCSpecialCase)
        //        {
        //            sb.Append("@*/");
        //        }
        //    }
        //    return sb.ToString();
        //}
    }
}
