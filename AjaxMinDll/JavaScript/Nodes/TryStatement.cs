// try.cs
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
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class TryStatement : AstNode
    {
        private Block m_tryBlock;
        public Block TryBlock
        {
            get { return m_tryBlock; }
            set
            {
                if (value != m_tryBlock)
                {
                    if (m_tryBlock != null && m_tryBlock.Parent == this)
                    {
                        m_tryBlock.Parent = null;
                    }
                    m_tryBlock = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_catchBlock;
		public Block CatchBlock
        {
            get { return m_catchBlock; }
            set
            {
                if (value != m_catchBlock)
                {
                    if (m_catchBlock != null && m_catchBlock.Parent == this)
                    {
                        m_catchBlock.Parent = null;
                    }
                    m_catchBlock = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_finallyBlock;
		public Block FinallyBlock
        {
            get { return m_finallyBlock; }
            set
            {
                if (value != m_finallyBlock)
                {
                    if (m_finallyBlock != null && m_finallyBlock.Parent == this)
                    {
                        m_finallyBlock.Parent = null;
                    }
                    m_finallyBlock = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public string CatchVarName { get; private set; }
        public Context CatchVarContext { get; private set; }
        public Binding CatchBinding { get; set; }

        public TryStatement(Context context, JSParser parser, AstNode tryBlock, string catchVarName, Context catchVarContext, AstNode catchBlock, AstNode finallyBlock)
            : base(context, parser)
        {
            CatchVarName = catchVarName;
            TryBlock = ForceToBlock(tryBlock);
            CatchBlock = ForceToBlock(catchBlock);
            FinallyBlock = ForceToBlock(finallyBlock);

            CatchVarContext = catchVarContext;
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(TryBlock, CatchBlock, FinallyBlock);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (TryBlock == oldNode)
            {
                TryBlock = ForceToBlock(newNode);
                return true;
            }
            if (CatchBlock == oldNode)
            {
                CatchBlock = ForceToBlock(newNode);
                return true;
            }
            if (FinallyBlock == oldNode)
            {
                FinallyBlock = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        public override bool RequiresSeparator
        {
            get
            {
                // try requires no separator
                return false;
            }
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();

        //    // passing a "T" format means nested try's don't actually nest -- they 
        //    // just add the catch clauses to the end
        //    if (format != ToCodeFormat.NestedTry)
        //    {
        //        sb.Append("try");
        //        if (TryBlock == null)
        //        {
        //            // empty body
        //            sb.Append("{}");
        //        }
        //        else
        //        {
        //            sb.Append(TryBlock.ToCode(ToCodeFormat.NestedTry));
        //        }
        //    }
        //    else
        //    {
        //        sb.Append(TryBlock.ToCode(ToCodeFormat.NestedTry));
        //    }

        //    // handle the catch clause (if any)
        //    // catch should always have braces around it
        //    string catchString = (
        //        CatchBlock == null
        //        ? string.Empty
        //        : CatchBlock.Count == 0
        //            ? "{}"
        //            : CatchBlock.ToCode(ToCodeFormat.AlwaysBraces)
        //        );
        //    if (catchString.Length > 0)
        //    {
        //        Parser.Settings.NewLine(sb);
        //        sb.Append("catch(");
        //        sb.Append(CatchBinding != null ? CatchBinding.ToString() : CatchVarName);
        //        sb.Append(')');
        //        sb.Append(catchString);
        //    }

        //    // handle the finally, if any
        //    // finally should always have braces around it
        //    string finallyString = (
        //      FinallyBlock == null
        //      ? string.Empty
        //      : FinallyBlock.ToCode(ToCodeFormat.AlwaysBraces)
        //      );
        //    if (finallyString.Length > 0)
        //    {
        //        Parser.Settings.NewLine(sb);
        //        sb.Append("finally");
        //        sb.Append(finallyString);
        //    }
        //    return sb.ToString();
        //}
    }
}
