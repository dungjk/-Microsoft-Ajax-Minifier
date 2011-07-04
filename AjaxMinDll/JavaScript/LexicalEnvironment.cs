// LexicalEnvironment.cs
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
using System.Globalization;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript
{
    public abstract class LexicalEnvironment
    {
        public LexicalEnvironment Outer { get; private set; }
        public bool UseStrict { get; set; }
        public bool IsFunctionScope { get; set; }
        public bool IsKnownAtCompileTime { get; set; }
        public abstract bool IsGlobal { get; set; }

        private int m_placeholderIndex = 0;

        private Dictionary<string, Binding> m_definedWithin;
        public IList<Binding> DefinedWithin
        {
            get
            {
                return m_definedWithin == null ? null : new List<Binding>(m_definedWithin.Values);
            }
        }
        public int CountDefined { get { return m_definedWithin == null ? 0 : m_definedWithin.Count; } }

        private Dictionary<string, Reference> m_referencedWithin;
        public IList<Reference> ReferencedWithin
        {
            get
            {
                return m_referencedWithin == null ? null : new List<Reference>(m_referencedWithin.Values);
            }
        }

        private Dictionary<string, Reference> m_passThroughs;
        public IList<Reference> PassThroughReferences
        {
            get
            {
                return m_passThroughs == null ? null : new List<Reference>(m_passThroughs.Values);
            }
        }

        protected LexicalEnvironment(LexicalEnvironment outer)
        {
            Outer = outer;
            IsKnownAtCompileTime = true;
        }

        public bool MustRenameBindings
        {
            get
            {
                // assume we're good to go
                bool foundAny = false;
                if (m_definedWithin != null)
                {
                    // for each of the bindings we define
                    foreach (var binding in m_definedWithin.Values)
                    {
                        // if the name is invalid, then we need to rename it
                        if (!JSScanner.IsValidIdentifier(binding.Name))
                        {
                            // we found one; that's enough checking
                            foundAny = true;
                            break;
                        }
                    }
                }

                return foundAny;
            }
        }

        protected Binding AddDefinition(string name, Binding binding)
        {
            if (m_definedWithin == null)
            {
                m_definedWithin = new Dictionary<string,Binding>();
            }
            m_definedWithin.Add(name, binding);
            return binding;
        }

        public virtual Reference GetIdentifierReference(string name, Context context)
        {
            Reference reference;
            if (HasBinding(name))
            {
                // return a reference to THIS lexical environment.
                // don't add to pass-throughs because it's defined in THIS environment.
                reference = new Reference(name, this, context);
            }
            else if (Outer == null)
            {
                // top of the chain and it ain't here - this is an undefined reference.
                // create the binding in this scope (should be the global scope)
                // and return a new reference to it.
                reference = new Reference(name, null, context);
            }
            else
            {
                // ask the outer lex.
                reference = Outer.GetIdentifierReference(name, context);

                // add to the pass-throughs -- we don't define it and had to ask an
                // up-stream environent for a reference
                if (m_passThroughs == null)
                {
                    m_passThroughs = new Dictionary<string, Reference>();
                    m_passThroughs.Add(name, reference);
                }
                else if (!m_passThroughs.ContainsKey(name))
                {
                    m_passThroughs.Add(name, reference);
                }
            }
            return reference;
        }

        public Reference ResolveLookup(Lookup lookup)
        {
            Reference reference = null;
            if (lookup != null)
            {
                reference = GetIdentifierReference(lookup.Name, lookup.Context);
                lookup.Reference = reference;
                AddReference(reference);
            }
            return reference;
        }

        public void AddReference(Reference reference)
        {
            if (reference != null)
            {
                // tell the reference it has another cumulative reference
                reference.AddReference();

                // add the reference to this environment's list of references 
                // that are actually referenced from within this environment
                if (m_referencedWithin == null)
                {
                    m_referencedWithin = new Dictionary<string, Reference>();
                    m_referencedWithin.Add(reference.Name, reference);
                }
                else if (!m_referencedWithin.ContainsKey(reference.Name))
                {
                    m_referencedWithin.Add(reference.Name, reference);
                }
            }
        }

        public Binding CreatePlaceholder(Context declarationContext)
        {
            // the name needs to be unique
            string name;
            do
            {
                name = string.Format(CultureInfo.InvariantCulture, "[placeholder{0}]", ++m_placeholderIndex);
            }
            while (m_definedWithin.ContainsKey(name));

            // create it
            var binding = CreateMutableBinding(name, false);

            // set the appropriate binding state and return it
            binding.Category = BindingCategory.RenamingPlaceholder;
            binding.IsGenerated = true;
            binding.IsImmutable = true;
            binding.CanRename = true;
            binding.DefinitionContext = declarationContext;
            return binding;
        }

        public DeclarativeEnvironment NewDeclarativeEnvironment()
        {
            var newEnv = new DeclarativeEnvironment(this);
            newEnv.UseStrict = UseStrict;
            return newEnv;
        }

        public ObjectEnvironment NewObjectEnvironment(Type objectType)
        {
            var newEnv = new ObjectEnvironment(this, objectType);
            newEnv.UseStrict = UseStrict;
            return newEnv;
        }

        public virtual bool HasBinding(string name)
        {
            return m_definedWithin != null && m_definedWithin.ContainsKey(name);
        }

        public virtual bool TryGetBinding(string name, out Binding binding)
        {
            var success = false;
            if (m_definedWithin != null)
            {
                success = m_definedWithin.TryGetValue(name, out binding);
            }
            else
            {
                binding = null;
            }
            return success;
        }

        public abstract Binding CreateMutableBinding(string name, bool canBeDeleted);

        public virtual bool DeleteBinding(string name)
        {
            // if we don't have the name in our collection, we return true
            var success = true;
            Binding binding;
            if (TryGetBinding(name, out binding))
            {
                // we have it. See if we CAN delete it
                if (binding.CanDelete)
                {
                    // go ahead and remove it
                    m_definedWithin.Remove(name);
                }
                else
                {
                    // we have it, but we can't delete it
                    success = false;
                }
            }
            return success;
        }
    }
}
