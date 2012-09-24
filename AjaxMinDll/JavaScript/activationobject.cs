// activationobject.cs
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
using System.Globalization;
using System.Reflection;

namespace Microsoft.Ajax.Utilities
{
    public abstract class ActivationObject
    {
        private bool m_useStrict;//= false;
        public bool UseStrict
        {
            get
            {
                return m_useStrict;
            }
            set
            {
                // can set it to true, but can't set it to false
                if (value)
                {
                    // set our value
                    m_useStrict = value;

                    // and all our child scopes (recursive)
                    foreach (var child in ChildScopes)
                    {
                        child.UseStrict = value;
                    }
                }
            }
        }

        private bool m_isKnownAtCompileTime;
        public bool IsKnownAtCompileTime
        {
            get { return m_isKnownAtCompileTime; }
            set 
            { 
                m_isKnownAtCompileTime = value;
                if (!value 
                    && Parser.Settings.EvalTreatment == EvalTreatment.MakeAllSafe)
                {
                    // are we a function scope?
                    var funcScope = this as FunctionScope;
                    if (funcScope == null)
                    {
                        // we are not a function, so the parent scope is unknown too
                        Parent.IfNotNull(p => p.IsKnownAtCompileTime = false);
                    }
                    else
                    {
                        // we are a function, check to see if the function object is actually
                        // referenced. (we don't want to mark the parent as unknown if this function 
                        // isn't even referenced).
                        if (funcScope.FunctionObject.IsReferenced)
                        {
                            Parent.IsKnownAtCompileTime = false;
                        }
                    }
                }
            }
        }

        public Dictionary<string, JSVariableField> NameTable { get; private set; }
        public IEnumerable<JSVariableField> FieldTable { get { return NameTable.Values; } }

        protected JSParser Parser { get; private set; }
        public ActivationObject Parent { get; private set; }
        public IList<ActivationObject> ChildScopes { get; private set; }

        public IList<Lookup> ScopeLookups { get; private set; }
        public IList<VariableDeclaration> VariableDeclarations { get; private set; }
        public IList<FunctionObject> FunctionDeclarations { get; private set; }

        protected ActivationObject(ActivationObject parent, JSParser parser)
        {
            m_isKnownAtCompileTime = true;
            m_useStrict = false;

            Parent = parent;
            NameTable = new Dictionary<string, JSVariableField>();
            ChildScopes = new List<ActivationObject>();
            Parser = parser;

            ScopeLookups = new List<Lookup>();
            VariableDeclarations = new List<VariableDeclaration>();
            FunctionDeclarations = new List<FunctionObject>();

            // if our parent is a scope....
            if (parent != null)
            {
                // add us to the parent's list of child scopes
                parent.ChildScopes.Add(this);

                // if the parent is strict, so are we
                UseStrict = parent.UseStrict;
            }
        }

        internal void AnalyzeScope()
        {
            // check for unused local fields or arguments if this isn't the global scope
            if (!(this is GlobalScope))
            {
                foreach (var variableField in NameTable.Values)
                {
                    if ((variableField.FieldType == FieldType.Local || variableField.FieldType == FieldType.Argument)
                        && !variableField.IsReferenced
                        && variableField.OriginalContext != null)
                    {
                        var funcObject = variableField.FieldValue as FunctionObject;
                        if (funcObject != null)
                        {
                            // if there's no function name, do nothing
                            if (funcObject.Name != null)
                            {
                                // if the function name isn't a simple identifier, then leave it there and mark it as
                                // not renamable because it's probably one of those darn IE-extension event handlers or something.
                                if (JSScanner.IsValidIdentifier(funcObject.Name))
                                {
                                    // unreferenced function declaration.
                                    // hide it from the output if our settings say we can
                                    if (IsKnownAtCompileTime
                                        && funcObject.Parser.Settings.MinifyCode
                                        && funcObject.Parser.Settings.RemoveUnneededCode)
                                    {
                                        funcObject.HideFromOutput = true;
                                    }

                                    // and fire an error
                                    Context ctx = funcObject.IdContext;
                                    if (ctx == null) { ctx = variableField.OriginalContext; }
                                    ctx.HandleError(JSError.FunctionNotReferenced, false);
                                }
                                else
                                {
                                    // not a valid identifier name for this function. Don't rename it.
                                    variableField.CanCrunch = false;
                                }
                            }
                        }
                        else if (!variableField.IsGenerated)
                        {
                            if (variableField.FieldType == FieldType.Argument)
                            {
                                // we only want to throw this error if it's possible to remove it
                                // from the argument list. And that will only happen if there are
                                // no REFERENCED arguments after this one in the formal parameter list.
                                // Assertion: because this is an argument, this should be a function scope,
                                // let's walk up to the first function scope we find, just in case.
                                FunctionScope functionScope = this as FunctionScope;
                                if (functionScope == null)
                                {
                                    ActivationObject scope = this.Parent;
                                    while (scope != null)
                                    {
                                        functionScope = scope as FunctionScope;
                                        if (scope != null)
                                        {
                                            break;
                                        }
                                    }
                                }
                                if (functionScope == null || functionScope.IsArgumentTrimmable(variableField))
                                {
                                    variableField.OriginalContext.HandleError(
                                      JSError.ArgumentNotReferenced,
                                      false
                                      );
                                }
                            }
                            else if (variableField.OuterField == null || !variableField.OuterField.IsReferenced)
                            {
                                variableField.OriginalContext.HandleError(
                                  JSError.VariableDefinedNotReferenced,
                                  false
                                  );
                            }
                        }
                    }
                }
            }

            // rename fields if we need to
            ManualRenameFields();

            // recurse 
            foreach (var activationObject in ChildScopes)
            {
                activationObject.AnalyzeScope();
            }
        }

        private void ManualRenameFields()
        {
            // if the local-renaming kill switch is on, we won't be renaming ANYTHING, so we'll have nothing to do.
            if (Parser.Settings.IsModificationAllowed(TreeModifications.LocalRenaming))
            {
                // if the parser settings has a list of rename pairs, we will want to go through and rename
                // any matches
                if (Parser.Settings.HasRenamePairs)
                {
                    // go through the list of fields in this scope. Anything defined in the script that
                    // is in the parser rename map should be renamed and the auto-rename flag reset so
                    // we don't change it later.
                    foreach (var varField in NameTable.Values)
                    {
                        // don't rename outer fields (only actual fields), 
                        // and we're only concerned with global or local variables --
                        // those which are defined by the script (not predefined, not the arguments object)
                        if (varField.OuterField == null 
                            && (varField.FieldType != FieldType.Arguments && varField.FieldType != FieldType.Predefined))
                        {
                            // see if the name is in the parser's rename map
                            string newName = Parser.Settings.GetNewName(varField.Name);
                            if (!string.IsNullOrEmpty(newName))
                            {
                                // it is! Change the name of the field, but make sure we reset the CanCrunch flag
                                // or setting the "crunched" name won't work.
                                // and don't bother making sure the name doesn't collide with anything else that
                                // already exists -- if it does, that's the developer's fault.
                                // TODO: should we at least throw a warning?
                                varField.CanCrunch = true;
                                varField.CrunchedName = newName;

                                // and make sure we don't crunch it later
                                varField.CanCrunch = false;
                            }
                        }
                    }
                }

                // if the parser settings has a list of no-rename names, then we will want to also mark any
                // fields that match and are still slated to rename as uncrunchable so they won't get renamed.
                // if the settings say we're not going to renaming anything automatically (KeepAll), then we 
                // have nothing to do.
                if (Parser.Settings.LocalRenaming != LocalRenaming.KeepAll)
                {
                    foreach (var noRename in Parser.Settings.NoAutoRenameCollection)
                    {
                        // don't rename outer fields (only actual fields), 
                        // and we're only concerned with fields that can still
                        // be automatically renamed. If the field is all that AND is listed in
                        // the collection, set the CanCrunch to false
                        JSVariableField varField;
                        if (NameTable.TryGetValue(noRename, out varField)
                            && varField.OuterField == null
                            && varField.CanCrunch)
                        {
                            // no, we don't want to crunch this field
                            varField.CanCrunch = false;
                        }
                    }
                }
            }
        }

        #region crunching methods

        internal virtual void ValidateGeneratedNames()
        {
            // check all the variables defined within this scope.
            // we're looking for uncrunched generated fields.
            foreach (JSVariableField variableField in NameTable.Values)
            {
                if (variableField.IsGenerated
                    && variableField.CrunchedName == null)
                {
                    // we need to rename this field.
                    // first we need to walk all the child scopes depth-first
                    // looking for references to this field. Once we find a reference,
                    // we then need to add all the other variables referenced in those
                    // scopes and all above them (from here) so we know what names we
                    // can't use.
                    var avoidTable = new HashSet<string>();
                    GenerateAvoidList(avoidTable, variableField.Name);

                    // now that we have our avoid list, create a crunch enumerator from it
                    CrunchEnumerator crunchEnum = new CrunchEnumerator(avoidTable);

                    // and use it to generate a new name
                    variableField.CrunchedName = crunchEnum.NextName();
                }
            }

            // recursively traverse through our children
            foreach (ActivationObject scope in ChildScopes)
            {
                scope.ValidateGeneratedNames();
            }
        }

        private bool GenerateAvoidList(HashSet<string> table, string name)
        {
            // our reference flag is based on what was passed to us
            bool isReferenced = false;

            // depth first, so walk all the children
            foreach (ActivationObject childScope in ChildScopes)
            {
                // if any child returns true, then it or one of its descendents
                // reference this variable. So we reference it, too
                if (childScope.GenerateAvoidList(table, name))
                {
                    // we'll return true because we reference it
                    isReferenced = true;
                }
            }

            if (!isReferenced)
            {
                // none of our children reference the scope, so see if we do
                isReferenced = NameTable.ContainsKey(name);
            }

            if (isReferenced)
            {
                // if we reference the name or are in line to reference the name,
                // we need to add all the variables we reference to the list
                foreach (var variableField in NameTable.Values)
                {
                    table.Add(variableField.ToString());
                }
            }

            // return whether or not we are in the reference chain
            return isReferenced;
        }

        internal virtual void AutoRenameFields()
        {
            // if we're not known at compile time, then we can't crunch
            // the local variables in this scope, because we can't know if
            // something will reference any of it at runtime.
            // eval is something that will make the scope unknown because we
            // don't know what eval will evaluate to until runtime
            if (m_isKnownAtCompileTime)
            {
                // get an array of all the uncrunched local variables defined in this scope
                var localFields = GetUncrunchedLocals();
                if (localFields != null)
                {
                    // create a crunch-name enumerator, taking into account any fields within our
                    // scope that have already been crunched.
                    var avoidSet = new HashSet<string>();
                    foreach (var field in NameTable.Values)
                    {
                        // if the field can't be crunched, or if it can but we've already crunched it,
                        // add it to the avoid list so we don't reuse that name
                        if (!field.CanCrunch || field.CrunchedName != null)
                        {
                            avoidSet.Add(field.ToString());
                        }
                    }

                    var crunchEnum = new CrunchEnumerator(avoidSet);
                    foreach (var localField in localFields)
                    {
                        // if we are an unambiguous reference to a named function expression and we are not
                        // referenced by anyone else, then we can just skip this variable because the
                        // name will be stripped from the output anyway.
                        // we also always want to crunch "placeholder" fields.
                        if (localField.CanCrunch
                            && (localField.RefCount > 0 || localField.IsDeclared || localField.IsPlaceholder
                            || !(Parser.Settings.RemoveFunctionExpressionNames && Parser.Settings.IsModificationAllowed(TreeModifications.RemoveFunctionExpressionNames))))
                        {
                            localField.CrunchedName = crunchEnum.NextName();
                        }
                    }
                }
            }

            // then traverse through our children
            foreach (ActivationObject scope in ChildScopes)
            {
                scope.AutoRenameFields();
            }
        }

        internal IEnumerable<JSVariableField> GetUncrunchedLocals()
        {
            // there can't be more uncrunched fields than total fields
            var list = new List<JSVariableField>(NameTable.Count);
            foreach (var variableField in NameTable.Values)
            {
                // if the field is defined in this scope and hasn't been crunched
                // AND can still be crunched
                if (variableField != null && variableField.OuterField == null && variableField.CrunchedName == null
                    && variableField.CanCrunch)
                {
                    // if local renaming is not crunch all, then it must be crunch all but localization
                    // (we don't get called if we aren't crunching anything). 
                    // SO for the first clause:
                    // IF we are crunch all, we're good; but if we aren't crunch all, then we're only good if
                    //    the name doesn't start with "L_".
                    // The second clause is only computed IF we already think we're good to go.
                    // IF we aren't preserving function names, then we're good. BUT if we are, we're
                    // only good to go if this field doesn't represent a function object.
                    if ((Parser.Settings.LocalRenaming == LocalRenaming.CrunchAll
                        || !variableField.Name.StartsWith("L_", StringComparison.Ordinal))
                        && !(Parser.Settings.PreserveFunctionNames && variableField.IsFunction))
                    {
                        // don't add to our list if it's a function that's going to be hidden anyway
                        FunctionObject funcObject;
                        if (!variableField.IsFunction
                            || (funcObject = variableField.FieldValue as FunctionObject) == null
                            || !funcObject.HideFromOutput)
                        {
                            list.Add(variableField);
                        }
                    }
                }
            }

            if (list.Count == 0)
            {
                return null;
            }

            // sort the array and return it
            list.Sort(ReferenceComparer.Instance);
            return list;
        }

        #endregion

        #region field-management methods

        public virtual JSVariableField this[string name]
        {
            get
            {
                JSVariableField variableField;
                // check to see if this name is already defined in this scope
                if (!NameTable.TryGetValue(name, out variableField))
                {
                    // not in this scope
                    variableField = null;
                }
                return variableField;
            }
        }

        public JSVariableField FindReference(string name)
        {
            // see if we have it
            var variableField = this[name];

            // if we didn't find anything and this scope has a parent
            if (variableField == null)
            {
                if (this.Parent != null)
                {
                    // recursively go up the scope chain to find a reference,
                    // then create an inner field to point to it and we'll return
                    // that one.
                    variableField = CreateInnerField(this.Parent.FindReference(name));

                    // mark it as a placeholder. we might be going down a chain of scopes,
                    // where we will want to reserve the variable name, but not actually reference it.
                    // at the end where it is actually referenced we will reset the flag.
                    variableField.IsPlaceholder = true;
                }
                else
                {
                    // must be global scope. the field is undefined!
                    variableField = AddField(new JSVariableField(FieldType.UndefinedGlobal, name, 0, null));
                }
            }

            return variableField;
        }

        public virtual JSVariableField DeclareField(string name, object value, FieldAttributes attributes)
        {
            JSVariableField variableField;
            if (!NameTable.TryGetValue(name, out variableField))
            {
                variableField = CreateField(name, value, attributes);
                AddField(variableField);
            }
            return variableField;
        }

        public virtual JSVariableField CreateField(JSVariableField outerField)
        {
            // use the same type as the outer field by default
            return outerField.IfNotNull(o => new JSVariableField(o.FieldType, o));
        }

        public abstract JSVariableField CreateField(string name, object value, FieldAttributes attributes);

        public virtual JSVariableField CreateInnerField(JSVariableField outerField)
        {
            JSVariableField innerField = null;
            if (outerField != null)
            {
                if (outerField.FieldType == FieldType.Global 
                    || outerField.FieldType == FieldType.Predefined
                    || outerField.FieldType == FieldType.UndefinedGlobal)
                {
                    // if this is a global (defined or undefined) or predefined field, then just add the field itself
                    // to the local scope. We don't want to create a local reference.
                    innerField = outerField;
                }
                else
                {
                    // create a new inner field to be added to our scope
                    innerField = CreateField(outerField);
                }

                // add the field to our scope and return it
                AddField(innerField);
            }

            return innerField;
        }

        internal JSVariableField AddField(JSVariableField variableField)
        {
            // add it to our name table 
            NameTable[variableField.Name] = variableField;

            // set the owning scope to this is we are the outer field, or the outer field's
            // owning scope if this is an inner field
            variableField.OwningScope = variableField.OuterField == null ? this : variableField.OuterField.OwningScope;
            return variableField;
        }

        #endregion
    }
}