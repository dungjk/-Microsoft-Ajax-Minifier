// break.cs
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

using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class BreakStatement : AstNode
    {
        public int NestLevel { get; set; }
        public string Label { get; set; }

        public string AlternateLabel { get; set; }

        public BreakStatement(Context context, JSParser parser, string label)
            : base(context, parser)
        {
            Label = (label == null || label.Length == 0) ? null : label;
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append("break");
        //    if (Label != null)
        //    {
        //        sb.Append(' ');
        //        if (NestLevel >= 0
        //            && Parser.Settings.LocalRenaming != LocalRenaming.KeepAll
        //            && Parser.Settings.IsModificationAllowed(TreeModifications.LocalRenaming))
        //        {
        //            // hypercrunched -- only depends on nesting level
        //            sb.Append(BindingMinifier.CrunchedLabel(NestLevel));
        //        }
        //        else
        //        {
        //            // not hypercrunched -- just output label
        //            sb.Append(Label);
        //        }
        //    }

        //    return sb.ToString();
        //}
    }
}