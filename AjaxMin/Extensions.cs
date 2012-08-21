// Extensions.cs
//
// Copyright 2012 Microsoft Corporation
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

namespace Microsoft.Ajax.Utilities
{
    using System;
    using System.Globalization;

    internal static class Extensions
    {
        public static string FormatInvariant(this string format, params object[] args)
        {
            try
            {
                return format == null
                    ? string.Empty
                    : string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        public static string ToStringInvariant(this int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
