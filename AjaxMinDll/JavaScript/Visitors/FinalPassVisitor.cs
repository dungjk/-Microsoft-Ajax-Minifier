// FinalPassVisitor.cs
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
    internal class FinalPassVisitor : TreeVisitor
    {
        private JSParser m_parser;

        public static void Apply(Block program, JSParser parser)
        {
            // create the visitor and visit the first node
            var visitor = new FinalPassVisitor(parser);
            program.Accept(visitor);
        }

        private FinalPassVisitor(JSParser parser)
        {
            m_parser = parser;
        }

        public override void Visit(ConstantWrapper node)
        {
            if (node != null)
            {
                // no children, so don't bother calling the base.
                if (node.PrimitiveType == PrimitiveType.Boolean
                    && m_parser.Settings.IsModificationAllowed(TreeModifications.BooleanLiteralsToNotOperators))
                {
                    node.ReplaceSelf(new NumericUnary(
                        node.Context,
                        m_parser,
                        new ConstantWrapper(node.ToBoolean() ? 0 : 1, PrimitiveType.Number, node.Context, m_parser),
                        JSToken.LogicalNot));
                }
            }
        }

        public override void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                // if this is a generated variable and it's not referenced anywhere, 
                // then we should remove it now
                if (node.Binding != null && node.Binding.IsGenerated && node.Binding.ReferenceCount == 0)
                {
                    // remove the binding from the enclosing scope
                    node.EnclosingVariableEnvironment.DeleteBinding(node.Binding.Name);

                    // remove the var-decl from the parent.
                    // could mess up the parent's iterator counter if the iterator doesn't take a
                    // snapshot of the current list.
                    node.ReplaceSelf(null);
                }
                else
                {
                    // nah, it's good. recurse.
                    base.Visit(node);
                }
            }
        }
    }
}
