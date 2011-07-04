// ast.cs
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

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public abstract class AstNode
    {
        // this is used in the child enumeration for nodes that don't have any children
        private static readonly IEnumerable<AstNode> s_emptyChildrenCollection = new AstNode[0];

        public AstNode Parent { get; set; }
        public Context Context { get; set; }
        public JSParser Parser { get; private set; }

        protected AstNode(Context context, JSParser parser)
        {
            Parser = parser;
            if (Object.ReferenceEquals(context, null))
            {
                // generate a bogus context
                Context = new Context(parser);
            }
            else
            {
                Context = context;
            }
        }

        // this is overridden is the Expression object to return true
        public virtual bool IsExpression { get { return false; } }

        public bool IsDirectivePrologue { get; set; }

        protected Block ForceToBlock(AstNode astNode)
        {
            // if the node is null or already a block, then we're 
            // good to go -- just return it.
            Block block = astNode as Block;
            if (astNode == null || block != null)
            {
                return block;
            }

            // it's not a block, so create a new block, append the astnode
            // and return the block
            block = new Block(null, Parser);
            block.Append(astNode);
            return block;
        }

        public virtual string GetFunctionNameGuess(AstNode target)
        {
            // most objects serived from AST return an empty string
            return Parent == null || target == Parent ? string.Empty : Parent.GetFunctionNameGuess(this);
        }

        public virtual bool RequiresSeparator
        {
            get { return true; }
        }

        internal virtual bool EndsWithEmptyBlock
        {
            get { return false; }
        }

        internal virtual bool IsDebuggerStatement
        {
            get { return false; }
        }

        public virtual PrimitiveType FindPrimitiveType()
        {
            // by default, we don't know what the primitive type of this node is
            return PrimitiveType.Other;
        }

        public virtual IEnumerable<AstNode> Children
        {
            get { return s_emptyChildrenCollection; }
        }

        internal static IEnumerable<AstNode> EnumerateNonNullNodes(AstNode[] nodes)
        {
            for (int ndx = 0; ndx < nodes.Length; ++ndx)
            {
                if (nodes[ndx] != null)
                {
                    yield return nodes[ndx];
                }
            }
        }

        internal static IEnumerable<AstNode> EnumerateNonNullNodes(AstNode n1, AstNode n2 = null, AstNode n3 = null, AstNode n4 = null) {
            return EnumerateNonNullNodes(new[] { n1, n2, n3, n4 });
        }

        public bool IsWindowLookup
        {
            get
            {
                Lookup lookup = this as Lookup;
                return (lookup != null
                        && string.CompareOrdinal(lookup.Name, "window") == 0
                        && lookup.Reference.Base == Parser.GlobalScope);
            }
        }

        public virtual bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            return false;
        }

        public bool ReplaceSelf(AstNode replacementNode)
        {
            return Parent == null ? false : Parent.ReplaceChild(this, replacementNode);
        }

        public virtual AstNode LeftHandSide
        {
            get
            {
                // default is just to return ourselves
                return this;
            }
        }

        public virtual LexicalEnvironment EnclosingLexicalEnvironment
        {
            get
            {
                // if we don't have a parent, then return null.
                // otherwise, just ask our parent. Nodes with scope will override this property.
                return Parent != null ? Parent.EnclosingLexicalEnvironment : null;
            }
        }

        public virtual LexicalEnvironment EnclosingVariableEnvironment
        {
            get
            {
                // if we don't have a parent, then return null.
                // otherwise, just ask our parent. Nodes with scope will override this property.
                return Parent != null ? Parent.EnclosingVariableEnvironment : null;
            }
        }

        /// <summary>
        /// Abstract method to be implemented by every concrete class.
        /// Returns true of the other object is equivalent to this object
        /// </summary>
        /// <param name="otherNode"></param>
        /// <returns></returns>
        public virtual bool IsEquivalentTo(AstNode otherNode)
        {
            // by default nodes aren't equivalent to each other unless we know FOR SURE that they are
            return false;
        }

        /// <summary>
        /// Abstract method to be implemented by every concrete node class
        /// </summary>
        /// <param name="visitor">visitor to accept</param>
        public abstract void Accept(IVisitor visitor);
    }
}
