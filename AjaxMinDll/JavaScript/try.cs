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

namespace Microsoft.Ajax.Utilities
{
    public sealed class TryNode : AstNode
    {
		public Block TryBlock { get; private set; }
		public Block CatchBlock { get; private set; }
		public Block FinallyBlock { get; private set; }

        public ParameterDeclaration CatchParameter { get; private set; }
        public string CatchVarName
        {
            get
            {
                return CatchParameter.IfNotNull(v => v.Name);
            }
        }
        public Context CatchVarContext
        {
            get
            {
                return CatchParameter.IfNotNull(v => v.Context);
            }
        }

        public TryNode(Context context, JSParser parser, AstNode tryBlock, ParameterDeclaration catchParameter, AstNode catchBlock, AstNode finallyBlock)
            : base(context, parser)
        {
            CatchParameter = catchParameter;
            if (CatchParameter != null) { CatchParameter.Parent = this; }

            TryBlock = ForceToBlock(tryBlock);
            if (TryBlock != null) { TryBlock.Parent = this; }

            CatchBlock = ForceToBlock(catchBlock);
            if (CatchBlock != null) { CatchBlock.Parent = this; }

            FinallyBlock = ForceToBlock(finallyBlock);
            if (FinallyBlock != null) { FinallyBlock.Parent = this; }
        }

        public void SetCatchVariable(JSVariableField field)
        {
            CatchParameter.VariableField = field;
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
                return EnumerateNonNullNodes(TryBlock, CatchParameter, CatchBlock, FinallyBlock);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (TryBlock == oldNode)
            {
                TryBlock = ForceToBlock(newNode);
                if (TryBlock != null) { TryBlock.Parent = this; }
                return true;
            }
            if (CatchParameter == oldNode)
            {
                CatchParameter = newNode as ParameterDeclaration;
                if (CatchParameter != null) { CatchParameter.Parent = this; }
                return true;
            }
            if (CatchBlock == oldNode)
            {
                CatchBlock = ForceToBlock(newNode);
                if (CatchBlock != null) { CatchBlock.Parent = this; }
                return true;
            }
            if (FinallyBlock == oldNode)
            {
                FinallyBlock = ForceToBlock(newNode);
                if (FinallyBlock != null) { FinallyBlock.Parent = this; }
                return true;
            }
            return false;
        }

        internal override bool RequiresSeparator
        {
            get
            {
                // try requires no separator
                return false;
            }
        }
    }
}
