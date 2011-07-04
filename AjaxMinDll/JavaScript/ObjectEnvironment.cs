// ObjectEnvironment.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.Ajax.Utilities.JavaScript
{
    public class ObjectEnvironment : LexicalEnvironment
    {
        private Type m_objectType;

        private bool m_isGlobal;
        public override bool IsGlobal
        {
            get
            {
                return m_isGlobal;
            }
            set
            {
                m_isGlobal = value;
            }
        }

        public ObjectEnvironment(LexicalEnvironment parent, Type objectType)
            : base(parent)
        {
            m_objectType = objectType;
        }

        public override Reference GetIdentifierReference(string name, Context context)
        {
            // do the default behavior
            var reference = base.GetIdentifierReference(name, context);

            // the global scope has an object type; with-scopes don't.
            // If the reference resolved and this is a with-scope, we need to 
            // mark it as NOT minifiable because it *could* be a property on our object.
            if (m_objectType == null)
            {
                if (reference != null)
                {
                    if (reference.Base != null)
                    {
                        // if we're the base, then everything is hunky-dorey.
                        // but if we're not, and this reference might point to something
                        // OUTSIDE this scope should the with-object not have a property with
                        // this name....
                        if (reference.Base != this)
                        {
                            // get the outer binding
                            Binding binding;
                            if (reference.Base.TryGetBinding(name, out binding))
                            {
                                // mark it as not minifiable so it stays in-sync with any potential object property
                                binding.CanRename = false;

                                // we're going to change this reference to point to this scope so we can
                                // keep track of the potential properties we might reference on the object,
                                // BUT, we need to make sure the outer object knows this might reference it
                                // if the object doesn't have a property of that name. So bump up the reference count.
                                ++binding.ReferenceCount;
                            }

                            // and set the base to us so it attempts to resolve to the object later
                            reference.Base = this;
                        }
                    }
                    else
                    {
                        // undefined reference - let's change it to be a reference into our
                        // "object" property collection so it doesn't get flagged as undefined.
                        // hopefully at runtime this resolves to the with-object or it could throw
                        // an error
                        // TODO: very low-pri warning?
                        reference.Base = this;
                    }
                }
                else
                {
                    // should never happen, but just in case....
                    reference = new Reference(name, this, context);
                }
            }

            return reference;
        }

        private bool ObjectHasPropertyName(string name)
        {
            // gotta have an object for it to have a property
            var exists = false;
            if (m_objectType != null)
            {
                // can be property, field, event, or method, and only
                // on the exact type given to us -- no inherited members.
                // always returns an array, never null.
                exists = m_objectType.GetMember(
                    name, 
                    MemberTypes.Event | MemberTypes.Field | MemberTypes.Property | MemberTypes.Method, 
                    BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Length > 0;
            }
            return exists;
        }

        public override bool HasBinding(string name)
        {
            try
            {
                // return true if there is a property name, or if the
                // reference map contains an entry
                return base.HasBinding(name) || ObjectHasPropertyName(name);
            }
            catch (ArgumentNullException)
            {
                // shouldn't pass null as the name!
                return false;
            }
            catch (AmbiguousMatchException)
            {
                // if it's ambiguous, that means there's at least one, right?
                return true;
            }
        }

        public override bool TryGetBinding(string name, out Binding binding)
        {
            var success = false;
            Binding foundBinding = null;
            if (HasBinding(name))
            {
                if (!base.TryGetBinding(name, out foundBinding))
                {
                    // we don't already have a reference for this name in our collection
                    foundBinding = new Binding(name);
                    foundBinding.CanRename = false;

                    // if object type is null, we are in a with-scope and this is a property binding.
                    // otherwise we are in a global scope and this is a predefined binding.
                    foundBinding.Category = m_objectType == null
                        ? BindingCategory.Property
                        : BindingCategory.Predefined;

                    // make sure the collection is created, and add the new binding to it
                    AddDefinition(name, foundBinding);
                }

                success = true;
            }
            else if (m_objectType == null)
            {
                // we are trying to resolve to this with-scope, but there isn't anything
                // defined yet. We need to create a binding that will be the ASSUMED property
                // on the with-object
                foundBinding = CreateMutableBinding(name, true);

                // link it to any outside bindings, in case there are any
                if (Outer != null)
                {
                    foundBinding.Linked = Outer.GetIdentifierReference(name, null);
                }
            }

            binding = foundBinding;
            return success;
        }

        public override Binding CreateMutableBinding(string name, bool canBeDeleted)
        {
            // create a binding and add it to our collection
            var binding = new Binding(name);
            binding.CanDelete = true;
            binding.IsConfigurable = canBeDeleted;
            binding.IsEnumerable = true;

            // if this is a global scope, the variable is normal; otherwise this
            // must be a with-scope and the type should be property
            binding.Category = m_objectType != null ? BindingCategory.Normal : BindingCategory.Property;

            // by default we won't minify these bindings -- they are either globals or properties
            binding.CanRename = false;

            // add it to the collection
            return AddDefinition(name, binding);
        }
    }
}
