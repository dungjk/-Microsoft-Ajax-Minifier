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
                WriteProgress(AjaxMin.GlobalObjectsHeader);
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
                        WriteBlockHeader(blockScope, AjaxMin.BlockTypeCatch);
                    }
                    else if (blockScope is WithScope)
                    {
                        WriteBlockHeader(blockScope, AjaxMin.BlockTypeWith);
                    }
                    else
                    {
                        WriteBlockHeader(blockScope, AjaxMin.BlockTypeLexical);
                    }
                }
            }

            // get all the fields in the scope
            List<JSVariableField> scopeFields = new List<JSVariableField>(scope.NameTable.Values);

            // sort the fields
            scopeFields.Sort(FieldComparer.Instance);

            // iterate over all the fields
            foreach (JSVariableField variableField in scopeFields)
            {
                // don't report placeholder fields or fields with the SpecialName attribute that aren't referenced
                if (!variableField.IsPlaceholder
                    && (variableField.Attributes != FieldAttributes.SpecialName || variableField.IsReferenced))
                {
                    WriteMemberReport(variableField);
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
                sb.Append(AjaxMin.NotKnown);
                sb.Append(']');
                knownMarker = sb.ToString();
            }

            WriteProgress();
            WriteProgress(AjaxMin.BlockScopeHeader.FormatInvariant(
              blockType,
              blockScope.Context.StartLineNumber,
              blockScope.Context.StartColumn + 1,
              knownMarker));
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
                crunched = AjaxMin.CrunchedTo.FormatInvariant(functionField.CrunchedName, functionField.RefCount);
            }

            // get the status if the function
            StringBuilder statusBuilder = new StringBuilder();
            if (!isKnown)
            {
                statusBuilder.Append('[');
                statusBuilder.Append(AjaxMin.NotKnown);
            }
            if (funcObj.FunctionScope.Parent is GlobalScope)
            {
                // global function.
                // if this is a named function expression, we still want to know if it's
                // referenced by anyone
                if (funcObj.FunctionType == FunctionType.Expression && !string.IsNullOrEmpty(funcObj.Name))
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
                    statusBuilder.Append(AjaxMin.FunctionInfoReferences.FormatInvariant(
                        funcObj.RefCount
                        ));
                }
            }
            else if (!funcObj.IsReferenced && m_useReferenceCounts)
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

                statusBuilder.Append(AjaxMin.Unreachable);
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

            var functionName = funcObj.Name;
            if (functionName.IsNullOrWhiteSpace())
            {
                functionName = !funcObj.NameGuess.IsNullOrWhiteSpace()
                    ? '"' + funcObj.NameGuess + '"'
                    : AjaxMin.AnonymousFunction;
            }

            // output
            WriteProgress();
            WriteProgress(AjaxMin.FunctionHeader.FormatInvariant(
                AjaxMin.ResourceManager.GetString(functionType, AjaxMin.Culture),
                functionName,
                funcObj.Context.StartLineNumber,
                funcObj.Context.StartColumn + 1,
                status,
                crunched));
        }

        // NAME [SCOPE TYPE] [crunched to CRUNCH]
        //
        // SCOPE: global, local, outer, ''
        // TYPE: var, function, argument, arguments array, possibly undefined
        private void WriteMemberReport(JSVariableField variableField)
        {
            // don't report arguments fields that aren't referenced because
            // we add those fields to the function scopes automatically
            if (variableField.FieldType != FieldType.Arguments || variableField.RefCount > 0)
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
                    crunched = AjaxMin.CrunchedTo.FormatInvariant(variableField.CrunchedName, variableField.RefCount);
                }
                else
                {
                    crunched = AjaxMin.MemberInfoReferences.FormatInvariant(variableField.RefCount);
                }

                // get the field's default scope and type
                GetFieldScopeType(variableField, out scope, out type);
                if (variableField.FieldType == FieldType.WithField)
                {
                    // if the field is a with field, we won't be using the crunched field (since
                    // those fields can't be crunched), so let's overload it with what the field
                    // could POSSIBLY be if the with object doesn't have a property of that name
                    if (variableField.OuterField != null)
                    {
                        // make sure we get the OUTERMOST outer ield
                        var outerField = variableField.OuterField;
                        while (outerField.OuterField != null)
                        {
                            outerField = outerField.OuterField;
                        }

                        string outerScope;
                        string outerType;
                        GetFieldScopeType(outerField, out outerScope, out outerType);
                        if (!outerScope.IsNullOrWhiteSpace() || !outerType.IsNullOrWhiteSpace())
                        {
                            crunched = AjaxMin.MemberInfoWithPossibly.FormatInvariant(outerScope, outerType);
                        }
                    }
                    else
                    {
                        // no outer field. Then it must be a lexically-declared field inside the with-statement's
                        // block. These are tricky! At run-time if you try to declare a function, const, or let
                        // inside the with-scope, and the with-object already has a property with that name, the
                        // declaration will throw a run-time error.
                        crunched = variableField.IsFunction ? AjaxMin.MemberInfoWithLexFunc : AjaxMin.MemberInfoWithLexDecl;
                    }
                }

                var definedLocation = string.Empty;
                var definedContext = GetFirstDeclaration(variableField);
                if (definedContext != null)
                {
                    definedLocation = AjaxMin.MemberInfoDefinedLocation.FormatInvariant(definedContext.StartLineNumber, definedContext.StartColumn + 1);
                }

                // format the entire string
                WriteProgress(AjaxMin.MemberInfoFormat.FormatInvariant(
                    name,
                    scope,
                    type,
                    crunched,
                    definedLocation
                    ));
            }
        }

        private static Context GetFirstDeclaration(JSVariableField variableField)
        {
            // only local fields that actually correspond to a declaration get the declaration
            // added to the declarations collection -- inner references don't get the declaration,
            // and neither do ghosted variables. So starting from this variable, walk up the 
            // outer-reference chain until we find on with at least one declaration. If we don't
            // find one, that's fine -- there probably isn't a declaration for it (predefined, for example).
            while (variableField != null && variableField.Declarations.Count == 0)
            {
                variableField = variableField.OuterField;
            }

            if (variableField != null)
            {
                foreach (var declaration in variableField.Declarations)
                {
                    // we only care about the FIRST declaration, so return the context of the name
                    return declaration.NameContext;
                }
            }

            // if we get here, there were no declarations
            return null;
        }

        private static void GetFieldScopeType(JSVariableField variableField, out string scope, out string type)
        {
            // default scope is blank
            scope = string.Empty;
            switch (variableField.FieldType)
            {
                case FieldType.Argument:
                    type = AjaxMin.MemberInfoTypeArgument;
                    if (variableField.IsOuterReference)
                    {
                        scope = AjaxMin.MemberInfoScopeOuter;
                    }
                    break;

                case FieldType.Arguments:
                    type = AjaxMin.MemberInfoTypeArguments;
                    if (variableField.IsOuterReference)
                    {
                        scope = AjaxMin.MemberInfoScopeOuter;
                    }
                    break;

                case FieldType.CatchError:
                    type = AjaxMin.MemberInfoTypeCatchEror;
                    if (variableField.IsOuterReference)
                    {
                        scope = AjaxMin.MemberInfoScopeOuter;
                    }
                    break;

                case FieldType.GhostCatch:
                case FieldType.GhostFunction:
                default:
                    // ghost fields -- ignore
                    type = string.Empty;
                    break;

                case FieldType.Global:
                case FieldType.UndefinedGlobal:
                    if ((variableField.Attributes & FieldAttributes.RTSpecialName) == FieldAttributes.RTSpecialName)
                    {
                        // this is a special "global." It might not be a global, but something referenced
                        // in a with scope somewhere down the line.
                        type = AjaxMin.MemberInfoPossiblyUndefined;
                    }
                    else if (variableField.FieldValue is FunctionObject)
                    {
                        if (variableField.GhostedField == null)
                        {
                            type = AjaxMin.MemberInfoGlobalFunction;
                        }
                        else
                        {
                            type = AjaxMin.MemberInfoFunctionExpression;
                        }
                    }
                    else if (variableField.InitializationOnly)
                    {
                        type = AjaxMin.MemberInfoGlobalConst;
                    }
                    else
                    {
                        type = AjaxMin.MemberInfoGlobalVar;
                    }
                    break;

                case FieldType.Local:
                    // type string
                    if (variableField.FieldValue is FunctionObject)
                    {
                        if (variableField.GhostedField == null)
                        {
                            type = AjaxMin.MemberInfoLocalFunction;
                        }
                        else
                        {
                            type = AjaxMin.MemberInfoFunctionExpression;
                        }
                    }
                    else if (variableField.IsLiteral)
                    {
                        type = AjaxMin.MemberInfoLocalLiteral;
                    }
                    else if (variableField.InitializationOnly)
                    {
                        type = AjaxMin.MemberInfoLocalConst;
                    }
                    else
                    {
                        type = AjaxMin.MemberInfoLocalVar;
                    }

                    // scope string
                    scope = variableField.IsOuterReference
                        ? AjaxMin.MemberInfoScopeOuter
                        : AjaxMin.MemberInfoScopeLocal;
                    break;

                case FieldType.Predefined:
                    scope = AjaxMin.MemberInfoScopeGlobalObject;
                    type = variableField.IsFunction
                        ? AjaxMin.MemberInfoBuiltInMethod
                        : AjaxMin.MemberInfoBuiltInProperty;
                    break;

                case FieldType.WithField:
                    type = AjaxMin.MemberInfoWithField;
                    if (variableField.IsOuterReference)
                    {
                        scope = AjaxMin.MemberInfoScopeOuter;
                    }
                    break;
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
                WriteProgress(AjaxMin.UndefinedGlobalHeader);
                foreach (UndefinedReferenceException ex in undefinedList)
                {
                    WriteProgress(AjaxMin.UndefinedInfo.FormatInvariant(
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
            /// Local Argument fields first
            /// Local Arguments object
            /// Local Fields defined
            /// Local Functions defined
            /// Outer Argument fields
            /// Outer Arguments object
            /// Outer fields referenced
            /// Outer Functions referenced
            /// Global fields referenced
            /// Global functions referenced
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
                if (obj.FieldType == FieldType.Argument || obj.FieldType == FieldType.CatchError)
                {
                    return obj.IsOuterReference ? FieldOrder.OuterArgumentReferenced : FieldOrder.LocalArgument;
                }
                if (obj.FieldType == FieldType.Arguments)
                {
                    return obj.IsOuterReference ? FieldOrder.OuterArgumentsObject : FieldOrder.LocalArgumentsObject;
                }

                if (obj.FieldType == FieldType.Global)
                {
                    return (
                      obj.FieldValue is FunctionObject
                      ? FieldOrder.GlobalFunctionReferenced
                      : FieldOrder.GlobalFieldReferenced
                      );
                }

                if (obj.FieldType == FieldType.Predefined)
                {
                    return FieldOrder.Predefined;
                }

                if (obj.FieldValue is FunctionObject)
                {
                    // function
                    return obj.IsOuterReference
                        ? FieldOrder.OuterFunctionReferenced
                        : FieldOrder.LocalFunctionDefined;
                }

                // field
                return obj.IsOuterReference
                    ? FieldOrder.OuterFieldReferenced
                    : FieldOrder.LocalFieldDefined;
            }

            private enum FieldOrder : int
            {
                LocalArgument = 0,
                LocalArgumentsObject,
                LocalFieldDefined,
                LocalFunctionDefined,
                OuterArgumentReferenced,
                OuterArgumentsObject,
                OuterFieldReferenced,
                OuterFunctionReferenced,
                GlobalFieldReferenced,
                GlobalFunctionReferenced,
                Predefined,
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
