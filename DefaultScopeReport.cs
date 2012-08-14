// DefaultScopeReport.cs
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
using System.IO;
using System.Reflection;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public sealed class DefaultScopeReport : IScopeReport
    {
        #region fields

        private TextWriter m_writer;
        private bool m_useReferenceCounts;

        #endregion

        #region IScopeReport Members

        public string Name
        {
            get { return "Default"; }
        }

        public void CreateReport(TextWriter writer, GlobalScope globalScope, bool useReferenceCounts)
        {
            if (writer != null && globalScope != null)
            {
                m_writer = writer;
                m_useReferenceCounts = useReferenceCounts;

                // output global scope report
                WriteScopeReport(null, globalScope);

                // generate a flat array of function scopes ordered by context line start
                ActivationObject[] scopes = GetAllFunctionScopes(globalScope);

                // for each function scope, output a scope report
                foreach (ActivationObject scope in scopes)
                {
                    FunctionScope funcScope = scope as FunctionScope;
                    WriteScopeReport(
                      (funcScope != null ? funcScope.FunctionObject : null),
                      scope);
                }

                // write the unreferenced global report
                WriteUnrefedReport(globalScope);
                m_writer.Flush();
                m_writer = null;
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_writer != null)
            {
                m_writer.Flush();
                m_writer = null;
            }
        }

        #endregion

        #region private methods

        private ActivationObject[] GetAllFunctionScopes(GlobalScope globalScope)
        {
            // create a list to hold all the scopes
            List<ActivationObject> scopes = new List<ActivationObject>();

            // recursively add all the function scopes to the list
            AddScopes(scopes, globalScope);

            // sort the scopes by starting line (from the context)
            scopes.Sort(ScopeComparer.Instance);

            // return as an array
            return scopes.ToArray();
        }

        private void AddScopes(List<ActivationObject> list, ActivationObject parentScope)
        {
            // for each child scope...
            foreach (ActivationObject scope in parentScope.ChildScopes)
            {
                // add the scope to the list if it's not a globalscopes
                // which leaves function scopes and block scopes (from catch blocks)
                if (!(scope is GlobalScope))
                {
                    list.Add(scope);
                }
                // recurse...
                AddScopes(list, scope);
            }
        }

        private void WriteScopeReport(FunctionObject funcObj, ActivationObject scope)
        {
            // output the function header
            if (scope is GlobalScope)
            {
                WriteProgress(StringMgr.GetString("GlobalObjectsHeader"));
            }
            else
            {
                FunctionScope functionScope = scope as FunctionScope;
                if (functionScope != null && funcObj != null)
                {
                    WriteFunctionHeader(funcObj, scope.IsKnownAtCompileTime);
                }
                else
                {
                    BlockScope blockScope = scope as BlockScope;
                    if (blockScope is CatchScope)
                    {
                        WriteBlockHeader(blockScope, StringMgr.GetString("BlockTypeCatch"));
                    }
                    else if (blockScope is WithScope)
                    {
                        WriteBlockHeader(blockScope, StringMgr.GetString("BlockTypeWith"));
                    }
                    else
                    {
                        WriteProgress();
                        WriteProgress(StringMgr.GetString("UnknownScopeType", scope.GetType().ToString()));
                    }
                }
            }

            // get all the fields in the scope
            JSVariableField[] scopeFields = scope.GetFields();
            // sort the fields
            Array.Sort(scopeFields, FieldComparer.Instance);

            // iterate over all the fields
            foreach (JSVariableField variableField in scopeFields)
            {
                // don't report placeholder fields or fields with the SpecialName attribute that aren't referenced
                if (!variableField.IsPlaceholder
                    && (variableField.Attributes != FieldAttributes.SpecialName || variableField.IsReferenced))
                {
                    WriteMemberReport(variableField, scope);
                }
            }
        }

        private void WriteBlockHeader(BlockScope blockScope, string blockType)
        {
            string knownMarker = string.Empty;
            if (!blockScope.IsKnownAtCompileTime)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append('[');
                sb.Append(StringMgr.GetString("NotKnown"));
                sb.Append(']');
                knownMarker = sb.ToString();
            }

            WriteProgress();
            WriteProgress(StringMgr.GetString(
              "BlockScopeHeader",
              blockType,
              blockScope.Context.StartLineNumber,
              blockScope.Context.StartColumn + 1,
              knownMarker
              ));
        }

        //TYPE "NAME" - Starts at line LINE, col COLUMN STATUS [crunched to CRUNCH]
        //
        //TYPE: Function, Function getter, Function setter
        //STATUS: '', Unknown, Unreachable
        private void WriteFunctionHeader(FunctionObject funcObj, bool isKnown)
        {
            // get the crunched value (if any)
            string crunched = string.Empty;
            var functionField = funcObj.VariableField;
            if (functionField != null && functionField.CrunchedName != null)
            {
                crunched = StringMgr.GetString("CrunchedTo", functionField.CrunchedName, functionField.RefCount);
            }

            // get the status if the function
            StringBuilder statusBuilder = new StringBuilder();
            if (!isKnown)
            {
                statusBuilder.Append('[');
                statusBuilder.Append(StringMgr.GetString("NotKnown"));
            }
            if (funcObj.FunctionScope.Parent is GlobalScope)
            {
                // global function.
                // if this is a named function expression, we still want to know if it's
                // referenced by anyone
                if (funcObj.FunctionType == FunctionType.Expression
                    && !string.IsNullOrEmpty(funcObj.Name))
                {
                    // output a comma separator if not the first item, otherwise 
                    // open the square bracket
                    if (statusBuilder.Length > 0)
                    {
                        statusBuilder.Append(", ");
                    }
                    else
                    {
                        statusBuilder.Append('[');
                    }
                    statusBuilder.Append(StringMgr.GetString(
                        "FunctionInfoReferences",
                        funcObj.RefCount
                        ));
                }
            }
            else if (!funcObj.FunctionScope.IsReferenced(null) && m_useReferenceCounts)
            {
                // local function that isn't referenced -- unreachable!
                // output a comma separator if not the first item, otherwise 
                // open the square bracket
                if (statusBuilder.Length > 0)
                {
                    statusBuilder.Append(", ");
                }
                else
                {
                    statusBuilder.Append('[');
                }

                statusBuilder.Append(StringMgr.GetString("Unreachable"));
            }

            if (statusBuilder.Length > 0)
            {
                statusBuilder.Append(']');
            }

            string status = statusBuilder.ToString();
            string functionType;
            switch (funcObj.FunctionType)
            {
                case FunctionType.Getter:
                    functionType = "FunctionTypePropGet";
                    break;

                case FunctionType.Setter:
                    functionType = "FunctionTypePropSet";
                    break;

                case FunctionType.Expression:
                    functionType = "FunctionTypeExpression";
                    break;

                default:
                    functionType = "FunctionTypeFunction";
                    break;
            }

            // output
            WriteProgress();
            WriteProgress(StringMgr.GetString(
              "FunctionHeader",
              StringMgr.GetString(functionType),
              funcObj.Name,
              funcObj.Context.StartLineNumber,
              funcObj.Context.StartColumn + 1,
              status,
              crunched
              ));
        }

        // NAME [SCOPE TYPE] [crunched to CRUNCH]
        //
        // SCOPE: global, local, outer, ''
        // TYPE: var, function, argument, arguments array, possibly undefined
        private void WriteMemberReport(JSVariableField variableField, ActivationObject immediateScope)
        {
            string scope = string.Empty;
            string type = string.Empty;
            string crunched = string.Empty;
            string name = variableField.Name;
            if (variableField.IsLiteral)
            {
                name = variableField.FieldValue.ToString();
            }

            // calculate the crunched label
            if (variableField.CrunchedName != null)
            {
                crunched = StringMgr.GetString("CrunchedTo", variableField.CrunchedName, variableField.RefCount);
            }

            // get the field's default scope and type
            GetFieldScopeType(variableField, immediateScope, out scope, out type);
            if (variableField.FieldType == FieldType.WithField)
            {
                // if the field is a with field, we won't be using the crunched field (since
                // those fields can't be crunched), so let's overload it with what the field
                // could POSSIBLY be if the with object doesn't have a property of that name
                string outerScope;
                string outerType;
                GetFieldScopeType(variableField.OuterField, immediateScope, out outerScope, out outerType);
                crunched = StringMgr.GetString("MemberInfoWithPossibly", outerScope, outerType);
            }

            var definedLocation = string.Empty;
            var definedContext = (variableField.OuterField ?? variableField).OriginalContext;
            if (definedContext != null)
            {
                definedLocation = StringMgr.GetString("MemberInfoDefinedLocation", definedContext.StartLineNumber, definedContext.StartColumn + 1);
            }

            // format the entire string
            WriteProgress(StringMgr.GetString(
                "MemberInfoFormat",
                name,
                scope,
                type,
                crunched,
                definedLocation
                ));
        }

        private static void GetFieldScopeType(JSVariableField variableField, ActivationObject immediateScope, out string scope, out string type)
        {
            // default scope is blank
            scope = string.Empty;

            if (variableField.FieldType == FieldType.Argument)
            {
                type = StringMgr.GetString("MemberInfoTypeArgument");
            }
            else if (variableField.FieldType == FieldType.Arguments)
            {
                type = StringMgr.GetString("MemberInfoTypeArguments");
            }
            else if (variableField.FieldType == FieldType.Predefined)
            {
                scope = StringMgr.GetString("MemberInfoScopeGlobalObject");
                type = variableField.IsFunction
                    ? StringMgr.GetString("MemberInfoBuiltInMethod")
                    : StringMgr.GetString("MemberInfoBuiltInProperty");
            }
            else if (variableField.FieldType == FieldType.Global)
            {
                if ((variableField.Attributes & FieldAttributes.RTSpecialName) == FieldAttributes.RTSpecialName)
                {
                    // this is a special "global." It might not be a global, but something referenced
                    // in a with scope somewhere down the line.
                    type = StringMgr.GetString("MemberInfoPossiblyUndefined");
                }
                else if (variableField.FieldValue is FunctionObject)
                {
                    if (variableField.NamedFunctionExpression == null)
                    {
                        type = StringMgr.GetString("MemberInfoGlobalFunction");
                    }
                    else
                    {
                        type = StringMgr.GetString("MemberInfoFunctionExpression");
                    }
                }
                else if (variableField.InitializationOnly)
                {
                    type = StringMgr.GetString("MemberInfoGlobalConst");
                }
                else
                {
                    type = StringMgr.GetString("MemberInfoGlobalVar");
                }
            }
            else if (variableField.FieldType == FieldType.WithField)
            {
                type = StringMgr.GetString("MemberInfoWithField");
            }
            else if (variableField.FieldType == FieldType.NamedFunctionExpression)
            {
                type = StringMgr.GetString("MemberInfoSelfFuncExpr");
            }
            else if (variableField.FieldType == FieldType.Local)
            {
                // type string
                if (variableField.FieldValue is FunctionObject)
                {
                    if (variableField.NamedFunctionExpression == null)
                    {
                        type = StringMgr.GetString("MemberInfoLocalFunction");
                    }
                    else
                    {
                        type = StringMgr.GetString("MemberInfoFunctionExpression");
                    }
                }
                else if (variableField.IsLiteral)
                {
                    type = StringMgr.GetString("MemberInfoLocalLiteral");
                }
                else if (variableField.InitializationOnly)
                {
                    type = StringMgr.GetString("MemberInfoLocalConst");
                }
                else
                {
                    type = StringMgr.GetString("MemberInfoLocalVar");
                }

                // scope string
                // this is a local variable, so there MUST be a non-null function scope passed
                // to us. That function scope will be the scope we are expecting local variables
                // to be defined in. If the field is defined in that scope, it's local -- otherwise
                // it must be an outer variable.
                JSVariableField scopeField = immediateScope[variableField.Name];
                if (scopeField == null || scopeField.OuterField != null)
                {
                    scope = StringMgr.GetString("MemberInfoScopeOuter");
                }
                else
                {
                    scope = StringMgr.GetString("MemberInfoScopeLocal");
                }
            }
            else
            {
                type = StringMgr.GetString("MemberInfoBuiltInObject");
            }
        }

        private void WriteUnrefedReport(GlobalScope globalScope)
        {
            if (globalScope.UndefinedReferences != null && globalScope.UndefinedReferences.Count > 0)
            {
                // sort the undefined reference exceptions
                var undefinedList = new List<UndefinedReferenceException>(globalScope.UndefinedReferences);
                undefinedList.Sort(UndefinedComparer.Instance);

                // write the report
                WriteProgress();
                WriteProgress(StringMgr.GetString("UndefinedGlobalHeader"));
                foreach (UndefinedReferenceException ex in undefinedList)
                {
                    WriteProgress(StringMgr.GetString(
                      "UndefinedInfo",
                      ex.Name,
                      ex.Line,
                      ex.Column,
                      ex.ReferenceType.ToString()
                      ));
                }
            }
        }

        #endregion

        #region output methods

        private void WriteProgress()
        {
            WriteProgress(string.Empty);
        }

        private void WriteProgress(string format, params object[] args)
        {
            try
            {
                m_writer.WriteLine(format, args);
            }
            catch (FormatException)
            {
                m_writer.WriteLine(format);
            }
        }

        #endregion

        #region Comparer classes

        private class ScopeComparer : IComparer<ActivationObject>
        {
            // singleton instance
            public static readonly IComparer<ActivationObject> Instance = new ScopeComparer();

            // private constructor -- use singleton
            private ScopeComparer() { }

            #region IComparer<ActivationObject> Members

            public int Compare(ActivationObject left, ActivationObject right)
            {
                int comparison = 0;
                Context leftContext = GetContext(left);
                Context rightContext = GetContext(right);
                if (leftContext == null)
                {
                    // if they're both null, return 0 (equal)
                    // otherwise just the left is null, so we want it at the end, so
                    // return 1 to indicate that it goes after the right context
                    return (rightContext == null ? 0 : 1);
                }
                else if (rightContext == null)
                {
                    // return -1 to indicate that the right context (null) goes after the left
                    return -1;
                }

                // compare their start lines
                comparison = leftContext.StartLineNumber - rightContext.StartLineNumber;
                if (comparison == 0)
                {
                    comparison = leftContext.StartColumn - rightContext.StartColumn;
                }
                return comparison;
            }

            private static Context GetContext(ActivationObject obj)
            {
                FunctionScope funcScope = obj as FunctionScope;
                if (funcScope != null && funcScope.FunctionObject != null)
                {
                    return funcScope.FunctionObject.Context;
                }
                else
                {
                    BlockScope blockScope = obj as BlockScope;
                    if (blockScope != null)
                    {
                        return blockScope.Context;
                    }
                }
                return null;
            }

            #endregion
        }

        private class FieldComparer : IComparer<JSVariableField>
        {
            // singleton instance
            public static readonly IComparer<JSVariableField> Instance = new FieldComparer();

            // private constructor -- use singleton
            private FieldComparer() { }

            #region IComparer<JSVariableField> Members

            /// <summary>
            /// Argument fields first
            /// Fields defined
            /// Functions defined
            /// Globals referenced
            /// Outer fields referenced
            /// Functions referenced
            /// </summary>
            /// <param name="x">left-hand object</param>
            /// <param name="y">right-hand object</param>
            /// <returns>&gt;0 left before right, &lt;0 right before left</returns>
            public int Compare(JSVariableField left, JSVariableField right)
            {
                int comparison = 0;
                if (left != null && right != null)
                {
                    // compare type class
                    comparison = GetOrderIndex(left) - GetOrderIndex(right);
                    if (comparison == 0)
                    {
                        // sort alphabetically
                        comparison = string.Compare(
                          left.Name,
                          right.Name,
                          StringComparison.OrdinalIgnoreCase
                          );
                    }
                }
                return comparison;
            }

            #endregion

            private static FieldOrder GetOrderIndex(JSVariableField obj)
            {
                if (obj.FieldType == FieldType.Argument)
                {
                    return FieldOrder.Argument;
                }
                if (obj.FieldType == FieldType.Arguments)
                {
                    return FieldOrder.ArgumentsArray;
                }

                if (obj.FieldType == FieldType.Global)
                {
                    return (
                      obj.FieldValue is FunctionObject
                      ? FieldOrder.GlobalFunctionReferenced
                      : FieldOrder.GlobalFieldReferenced
                      );
                }

                if (obj.OuterField != null)
                {
                    return (obj.FieldValue is FunctionObject
                        ? FieldOrder.OuterFunctionReferenced
                        : FieldOrder.OuterFieldReferenced);
                }
                else
                {
                    return (obj.FieldValue is FunctionObject
                        ? FieldOrder.FunctionDefined
                        : FieldOrder.FieldDefined);
                }
            }

            private enum FieldOrder : int
            {
                Argument = 0,
                ArgumentsArray,
                FieldDefined,
                FunctionDefined,
                OuterFieldReferenced,
                OuterFunctionReferenced,
                GlobalFieldReferenced,
                GlobalFunctionReferenced,
                Other
            }
        }

        private class UndefinedComparer : IComparer<UndefinedReferenceException>
        {
            // singleton instance
            public static readonly IComparer<UndefinedReferenceException> Instance = new UndefinedComparer();

            // private constructor -- use singleton
            private UndefinedComparer() { }

            #region IComparer<UndefinedReferenceException> Members

            public int Compare(UndefinedReferenceException left, UndefinedReferenceException right)
            {
                // first do the right thing if one or both are null
                if (left == null && right == null)
                {
                    // both null -- equal
                    return 0;
                }

                if (left == null)
                {
                    // left is null, right is not -- left is less
                    return -1;
                }

                if (right == null)
                {
                    // left is not null, right is -- left is more
                    return 1;
                }

                // neither are null
                int comparison = string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
                if (comparison == 0)
                {
                    comparison = left.Line - right.Line;
                    if (comparison == 0)
                    {
                        comparison = left.Column - right.Column;
                    }
                }

                return comparison;
            }

            #endregion
        }

        #endregion
    }
}
