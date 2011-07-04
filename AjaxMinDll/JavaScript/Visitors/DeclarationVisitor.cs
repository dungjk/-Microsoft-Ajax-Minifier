// DeclarationVisitor.cs
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
using System.Collections.ObjectModel;

using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript.Visitors
{
    public class DeclarationVisitor : TreeVisitor
    {
        private List<FunctionObject> m_functions;
        public ReadOnlyCollection<FunctionObject> Functions
        {
            get
            {
                return m_functions != null
                    ? new ReadOnlyCollection<FunctionObject>(m_functions)
                    : null;
            }
        }

        private List<VariableDeclaration> m_variableDeclarations;
        public ReadOnlyCollection<VariableDeclaration> VariableDeclarations
        {
            get
            {
                return m_variableDeclarations != null
                    ? new ReadOnlyCollection<VariableDeclaration>(m_variableDeclarations)
                    : null;
            }
        }

        public static DeclarationVisitor Apply(AstNode node)
        {
            // create the visitor, apply it to the passed node, and then return the visitor
            var visitor = new DeclarationVisitor();
            if (node != null)
            {
                node.Accept(visitor);
            }
            return visitor;
        }

        private DeclarationVisitor()
        {
            // don't create the lists unless we need them
        }

        public override void Visit(FunctionObject node)
        {
            if (node != null)
            {
                // we are only interested in declarations and named expressions.
                if ((node.FunctionType == FunctionType.Declaration 
                    || node.FunctionType == FunctionType.Expression)
                    && !string.IsNullOrEmpty(node.Name))
                {
                    // create the list if we haven't already, and add the function to it
                    if (m_functions == null)
                    {
                        m_functions = new List<FunctionObject>();
                    }
                    m_functions.Add(node);
                }

                // NEVER recurse the function object! We are only interested in the current scope,
                // and a function object represents a new scope
            }
        }

        public override void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                // create the list if we haven't already, then add this declaration to it
                if (m_variableDeclarations == null)
                {
                    m_variableDeclarations = new List<VariableDeclaration>();
                }
                m_variableDeclarations.Add(node);

                // recurse the declaration
                base.Visit(node);
            }
        }
    }
}
