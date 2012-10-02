// parameterdeclaration.cs
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

namespace Microsoft.Ajax.Utilities
{
    public sealed class ParameterDeclaration : AstNode, INameDeclaration
    {
        private string m_name;

        public string Name
        {
            get
            {
                return (VariableField != null ? VariableField.ToString() : m_name);
            }
        }

        public string OriginalName
        {
            get { return m_name; }
        }

        public int Position { get; private set; }

        public JSVariableField VariableField { get; set; }

        public bool HasInitializer { get { return false; } }

        public Context NameContext { get { return Context; } }

        public ParameterDeclaration(Context context, JSParser parser, string identifier, int position)
            : base(context, parser)
        {
            m_name = identifier;
            Position = position;
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }
    }
}
