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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Microsoft.Ajax.Utilities
{
    public sealed class XmlScopeReport : IScopeReport
    {
        private XmlWriter m_writer;
        private bool m_useReferenceCounts;

        #region IScopeReport Members

        public string Name
        {
            get { return "Xml"; }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification="lower-case by design")]
        public void CreateReport(TextWriter writer, GlobalScope globalScope, bool useReferenceCounts)
        {
            if (globalScope != null)
            {
                m_useReferenceCounts = useReferenceCounts;

                // start the global scope
                m_writer = XmlWriter.Create(writer, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true });
                m_writer.WriteStartElement("global");

                // recursively process each child scope
                foreach (var childScope in globalScope.ChildScopes)
                {
                    ProcessScope(childScope);
                }

                // process any undefined references
                if (globalScope.UndefinedReferences != null && globalScope.UndefinedReferences.Count > 0)
                {
                    m_writer.WriteStartElement("undefined");

                    foreach (var undefined in globalScope.UndefinedReferences)
                    {
                        m_writer.WriteStartElement("reference");
                        m_writer.WriteAttributeString("name", undefined.Name);
                        m_writer.WriteAttributeString("type", undefined.ReferenceType.ToString().ToLowerInvariant());
                        m_writer.WriteAttributeString("srcLine", undefined.Line.ToStringInvariant());
                        m_writer.WriteAttributeString("srcCol", (undefined.Column + 1).ToStringInvariant());
                        m_writer.WriteEndElement();
                    }

                    m_writer.WriteEndElement();
                }

                m_writer.WriteEndElement();
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification="lower-case by design")]
        private void ProcessScope(ActivationObject scope)
        {
            var functionScope = scope as FunctionScope;
            if (functionScope != null)
            {
                m_writer.WriteStartElement("function");

                var functionObject = functionScope.FunctionObject;
                if (functionObject != null)
                {
                    m_writer.WriteAttributeString("type", functionObject.FunctionType.ToString().ToLowerInvariant());

                    if (string.IsNullOrEmpty(functionObject.Name))
                    {
                        if (functionObject.NameGuess.StartsWith("\"", StringComparison.Ordinal))
                        {
                            // strip enclosing quotes
                            m_writer.WriteAttributeString("guess", functionObject.NameGuess.Substring(1, functionObject.NameGuess.Length - 2));
                        }
                        else
                        {
                            m_writer.WriteAttributeString("guess", functionObject.NameGuess);
                        }
                    }
                    else
                    {
                        m_writer.WriteAttributeString("src", functionObject.Name);
                        if (functionObject.VariableField != null
                            && functionObject.VariableField.CrunchedName != null)
                        {
                            m_writer.WriteAttributeString("min", functionObject.VariableField.CrunchedName);
                        }
                    }

                    if (functionObject.Context != null)
                    {
                        m_writer.WriteAttributeString("srcLine", functionObject.Context.StartLineNumber.ToStringInvariant());
                        m_writer.WriteAttributeString("srcCol", (functionObject.Context.StartColumn + 1).ToStringInvariant());
                    }

                    if (m_useReferenceCounts && functionObject.VariableField != null)
                    {
                        var refCount = functionObject.VariableField.RefCount;
                        m_writer.WriteAttributeString("refcount", refCount.ToStringInvariant());

                        if (refCount == 0
                            && functionObject.FunctionType == FunctionType.Declaration
                            && functionObject.VariableField.FieldType == FieldType.Local)
                        {
                            // local function declaration with zero references? unreachable code!
                            m_writer.WriteAttributeString("unreachable", "true");
                        }
                    }

                    // add the arguments
                    m_writer.WriteStartElement("arguments");
                    if (functionObject.ParameterDeclarations != null)
                    {
                        foreach (var item in functionObject.ParameterDeclarations)
                        {
                            m_writer.WriteStartElement("argument");
                            if (item.Context != null)
                            {
                                m_writer.WriteAttributeString("srcLine", item.Context.StartLineNumber.ToStringInvariant());
                                m_writer.WriteAttributeString("srcCol", (item.Context.StartColumn + 1).ToStringInvariant());
                            }

                            var parameter = item as ParameterDeclaration;
                            if (parameter != null)
                            {
                                m_writer.WriteAttributeString("src", parameter.Name);
                                if (parameter.VariableField.CrunchedName != null)
                                {
                                    m_writer.WriteAttributeString("min", parameter.VariableField.CrunchedName);
                                }

                                if (m_useReferenceCounts && parameter.VariableField != null)
                                {
                                    m_writer.WriteAttributeString("refcount", parameter.VariableField.RefCount.ToStringInvariant());
                                }

                            }

                            m_writer.WriteEndElement();
                        }
                    }

                    m_writer.WriteEndElement();
                }
            }
            else if (scope is WithScope)
            {
                m_writer.WriteStartElement("with");
            }
            else if (scope is GlobalScope)
            {
                Debug.Fail("shouldn't get here!");
                m_writer.WriteStartElement("global");
            }
            else
            {
                var catchScope = scope as CatchScope;
                if (catchScope != null)
                {
                    m_writer.WriteStartElement("catch");

                    var catchVariable = catchScope.CatchParameter.VariableField;
                    m_writer.WriteStartElement("catchvar");
                    m_writer.WriteAttributeString("src", catchVariable.Name);
                    if (catchVariable.CrunchedName != null)
                    {
                        m_writer.WriteAttributeString("min", catchVariable.CrunchedName);
                    }

                    if (catchVariable.OriginalContext != null)
                    {
                        m_writer.WriteAttributeString("srcLine", catchVariable.OriginalContext.StartLineNumber.ToStringInvariant());
                        m_writer.WriteAttributeString("srcCol", (catchVariable.OriginalContext.StartColumn + 1).ToStringInvariant());
                    }

                    if (m_useReferenceCounts)
                    {
                        m_writer.WriteAttributeString("refcount", catchVariable.RefCount.ToStringInvariant());
                    }

                    m_writer.WriteEndElement();
                }
                else
                {
                    // must be generic block scope
                    Debug.Assert(scope is BlockScope);
                    m_writer.WriteStartElement("block");
                }
            }

            // process the defined and referenced fields
            ProcessFields(scope);

            // recursively process each child scope
            foreach (var childScope in scope.ChildScopes)
            {
                ProcessScope(childScope);
            }

            // close the element
            m_writer.WriteEndElement();
        }

        private void ProcessFields(ActivationObject scope)
        {
            // split fields into defined and referenced lists
            var definedFields = new List<JSVariableField>();
            var referencedFields = new List<JSVariableField>();
            foreach (var field in scope.NameTable.Values)
            {
                // if the field has no outer field reference, it is defined in this scope.
                // otherwise we're just referencing a field defined elsewhere
                if (field.OuterField == null)
                {
                    switch (field.FieldType)
                    {
                        case FieldType.Global:
                            if (scope is GlobalScope)
                            {
                                definedFields.Add(field);
                            }
                            else
                            {
                                referencedFields.Add(field);
                            }
                            break;

                        case FieldType.Local:
                            // defined within this scope
                            definedFields.Add(field);
                            break;

                        case FieldType.Argument:
                            // ignore the scope's arguments because we handle them separately
                            break;

                        case FieldType.CatchError:
                            // ignore the catch-scope's error parameter because we handle it separately
                            break;

                        case FieldType.Arguments:
                            if (field.RefCount > 0)
                            {
                                referencedFields.Add(field);
                            }
                            break;

                        case FieldType.UndefinedGlobal:
                        case FieldType.Predefined:
                        case FieldType.WithField:
                            referencedFields.Add(field);
                            break;

                        case FieldType.GhostFunction:
                        case FieldType.GhostCatch:
                            // ignore the ghost fields when reporting
                            break;
                    }
                }
                else
                {
                    // if the outer field is a placeholder, then we actually define it, not the outer scope
                    if (field.OuterField.IsPlaceholder)
                    {
                        if (field.FieldType != FieldType.Argument && field.FieldType != FieldType.CatchError)
                        {
                            definedFields.Add(field);
                        }
                    }
                    else
                    {
                        referencedFields.Add(field);
                    }
                }
            }

            if (definedFields.Count > 0)
            {
                m_writer.WriteStartElement("defines");
                foreach (var field in definedFields)
                {
                    ProcessField(field, true);
                }

                m_writer.WriteEndElement();
            }

            if (referencedFields.Count > 0)
            {
                m_writer.WriteStartElement("references");
                foreach (var field in referencedFields)
                {
                    ProcessField(field, false);
                }

                m_writer.WriteEndElement();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification="lower-case by design")]
        private void ProcessField(JSVariableField field, bool isDefined)
        {
            // save THIS field's refcount value because we will
            // be adjusting hte field pointer to be the outermost field
            // and we want to report THIS field's refcount, not the overall.
            var refCount = field.RefCount;
            var isGhost = false;

            // make sure we're at the outer-most field
            var isOuter = false;
            if (!isDefined)
            {
                while (field.OuterField != null)
                {
                    isOuter = true;
                    field = field.OuterField;
                }
            }

            m_writer.WriteStartElement("field");

            var typeValue = field.FieldType.ToString();
            switch (field.FieldType)
            {
                case FieldType.Argument:
                case FieldType.CatchError:
                case FieldType.WithField:
                    if (isOuter)
                    {
                        typeValue = "Outer " + typeValue;
                    }
                    break;

                case FieldType.Local:
                    if (isOuter)
                    {
                        typeValue = "Outer ";
                    }
                    else
                    {
                        typeValue = string.Empty;
                        if (field.IsPlaceholder || !field.IsDeclared)
                        {
                            isGhost = true;
                        }
                    }

                    if (field.IsFunction)
                    {
                        typeValue += "Function";
                    }
                    else
                    {
                        typeValue += "Variable";
                    }
                    break;

                case FieldType.Arguments:
                case FieldType.Global:
                case FieldType.UndefinedGlobal:
                case FieldType.GhostCatch:
                case FieldType.GhostFunction:
                case FieldType.Predefined:
                    break;
            }

            m_writer.WriteAttributeString("type", typeValue.ToLowerInvariant());
            m_writer.WriteAttributeString("src", field.Name);
            if (field.CrunchedName != null)
            {
                m_writer.WriteAttributeString("min", field.CrunchedName);
            }

            if (field.OriginalContext != null)
            {
                m_writer.WriteAttributeString("srcLine", field.OriginalContext.StartLineNumber.ToStringInvariant());
                m_writer.WriteAttributeString("srcCol", (field.OriginalContext.StartColumn + 1).ToStringInvariant());
            }

            if (m_useReferenceCounts)
            {
                m_writer.WriteAttributeString("refcount", refCount.ToStringInvariant());
            }

            if (field.IsAmbiguous)
            {
                m_writer.WriteAttributeString("ambiguous", "true");
            }

            if (field.IsGenerated)
            {
                m_writer.WriteAttributeString("generated", "true");
            }

            if (isGhost)
            {
                m_writer.WriteAttributeString("ghost", "true");
            }

            m_writer.WriteEndElement();
        }

        #endregion
    }
}
