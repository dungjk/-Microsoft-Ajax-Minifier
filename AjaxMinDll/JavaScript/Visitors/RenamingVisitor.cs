// RenamingVisitor.cs
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
    public class RenamingVisitor : TreeVisitor
    {
        private bool m_renameAll;

        public static void Rename(AstNode node, bool minifyAll)
        {
            if (node != null)
            {
                // create the visitor and visit the first node
                var visitor = new RenamingVisitor(minifyAll);
                node.Accept(visitor);
            }
        }

        private RenamingVisitor(bool renameAll)
        {
            m_renameAll = renameAll;
        }

        private void RenameBindings(LexicalEnvironment environment)
        {
            if (environment != null)
            {
                // if there are no defined bindings, we can bail right now.
                if (environment.CountDefined > 0)
                {
                    if (environment.IsKnownAtCompileTime || environment.MustRenameBindings)
                    {
                        // see if we want to rename all our variables
                        var renameAll = environment.IsKnownAtCompileTime && m_renameAll;

                        // create a list into which we'll stick all bindings we CAN rename
                        var renameBindings = new List<Binding>(m_renameAll ? environment.CountDefined : 0);

                        // and a list of references for names we can't re-use
                        // (seed it with the pass-through references
                        var avoidCollisions = new List<Reference>();
                        if (environment.PassThroughReferences != null)
                        {
                            avoidCollisions.AddRange(environment.PassThroughReferences);
                        }

                        foreach (var binding in environment.DefinedWithin)
                        {
                            // if we want to rename and we can, add it to the list of bindings we will rename.
                            // or, if the name is invalid, we're gong to want to minify it 
                            // (our generated bindings will have invalid names)
                            if ((renameAll || !JSScanner.IsValidIdentifier(binding.Name)) && binding.CanRename)
                            {
                                // if this binding has a linked reference
                                if (binding.Linked != null)
                                {
                                    // and add the reference to the avoid list
                                    avoidCollisions.Add(binding.Linked);

                                    // we actually want to keep the two bindings named the same thing
                                    Binding linkedBinding;
                                    if (binding.Linked.Base != null
                                        && binding.Linked.Base.TryGetBinding(binding.Linked.Name, out linkedBinding))
                                    {
                                        // set this binding to the same name as the linked binding. This assumes that we
                                        // have linked to an outer reference, which has already been renamed
                                        binding.AlternateName = linkedBinding.ToString();
                                    }
                                }
                                else
                                {
                                    // nope, all clear to be renamed
                                    renameBindings.Add(binding);
                                }
                            }
                            else
                            {
                                // we won't be renaming this - make sure we don't collide with its name
                                // by adding it to the list of avoids (create a new reference)
                                avoidCollisions.Add(new Reference(binding.Name, environment, null));
                            }
                        }


                        // if we are going to rename anything...
                        if (renameBindings.Count > 0)
                        {
                            // sort the bindings so we get a logical result
                            renameBindings.Sort(RenameBindingComparer.Instance);

                            // go ahead and create our binding minifier, giving it the list of
                            // pass-through bindings that we can't have collisions with
                            var minifier = new BindingMinifier(avoidCollisions);
                            foreach (var binding in renameBindings)
                            {
                                binding.AlternateName = minifier.NextName();
                            }
                        }
                    }
                }
            }
        }

        public override void Visit(Block node)
        {
            if (node != null)
            {
                // if this block has a lexical environment, rename its bindings
                RenameBindings(node.LexicalEnvironment);

                base.Visit(node);
            }
        }

        public override void Visit(BreakStatement node)
        {
            if (node != null)
            {
                if (node.NestLevel > 0 && m_renameAll)
                {
                    // the minified label depends entirely on the nest level
                    node.AlternateLabel = BindingMinifier.CrunchedLabel(node.NestLevel);
                }
            }
        }

        public override void Visit(ContinueStatement node)
        {
            if (node != null)
            {
                if (node.NestLevel > 0 && m_renameAll)
                {
                    // the minified label depends entirely on the nest level
                    node.AlternateLabel = BindingMinifier.CrunchedLabel(node.NestLevel);
                }
            }
        }

        public override void Visit(FunctionObject node)
        {
            if (node != null)
            {
                if (node.FunctionType != FunctionType.Declaration
                    && !string.IsNullOrEmpty(node.Name))
                {
                    // named function expressions have an extra environment as the immediate outer
                    // that holds the function name
                    RenameBindings(node.LexicalEnvironment.Outer);
                }

                // rename the function scope's bindings
                RenameBindings(node.LexicalEnvironment);

                // recurse
                base.Visit(node);
            }
        }

        public override void Visit(LabeledStatement node)
        {
            if (node != null)
            {
                if (node.NestLevel > 0 && m_renameAll)
                {
                    // the minified label depends entirely on the nest level
                    node.AlternateLabel = BindingMinifier.CrunchedLabel(node.NestLevel);
                }

                // then do the default
                base.Visit(node);
            }
        }
    }
}
