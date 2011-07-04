// DeclarativeEnvironment.cs
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

namespace Microsoft.Ajax.Utilities.JavaScript
{
    public class DeclarativeEnvironment : LexicalEnvironment
    {
        private List<ThisLiteral> m_thisLiterals;
        public IList<ThisLiteral> ThisLiterals { get { return m_thisLiterals; } }

        // the global environment cannot be a declarative environment
        public override bool IsGlobal
        {
            get
            {
                return false;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public DeclarativeEnvironment(LexicalEnvironment parent)
            : base(parent)
        {
        }

        public override Binding CreateMutableBinding(string name, bool canBeDeleted)
        {
            // create the binding, add it to the definition map, and return it
            var binding = new Binding(name);
            binding.CanDelete = canBeDeleted;
            AddDefinition(name, binding);
            return binding;
        }

        public Binding CreateImmutableBinding(string name)
        {
            var binding = new Binding(name);
            binding.IsImmutable = true;
            return AddDefinition(name, binding);
        }

        public bool InitializeImmutableBinding(string name, AstNode value)
        {
            bool success = false;
            Binding binding;
            if (TryGetBinding(name, out binding)
                && binding.IsImmutable
                && !binding.IsInitialized)
            {
                binding.Value = value;
                binding.IsInitialized = true;
                success = true;
            }

            return success;
        }

        public void AddThisLiteral(ThisLiteral thisLiteral)
        {
            // if we haven't created the list yet, do so now.
            // then add the node to the list.
            if (m_thisLiterals == null)
            {
                m_thisLiterals = new List<ThisLiteral>();
            }
            m_thisLiterals.Add(thisLiteral);
        }
    }
}
