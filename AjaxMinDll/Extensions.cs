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

        public static bool TryParseSingleInvariant(this string text, out float number)
        {
            try
            {
                number = System.Convert.ToSingle(text, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                number = float.NaN;
                return false;
            }
            catch (OverflowException)
            {
                number = float.NaN;
                return false;
            }
        }

        public static bool TryParseIntInvariant(this string text, NumberStyles numberStyles, out int number)
        {
            number = default(int);
            return text == null ? false : int.TryParse(text, numberStyles, CultureInfo.InvariantCulture, out number);
        }

        public static bool TryParseLongInvariant(this string text, NumberStyles numberStyles, out long number)
        {
            number = default(long);
            return text == null ? false : long.TryParse(text, numberStyles, CultureInfo.InvariantCulture, out number);
        }

        public static string ToStringInvariant(this int number, string format = null)
        {
            return format == null
                ? number.ToString(CultureInfo.InvariantCulture)
                : number.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ToStringInvariant(this double number, string format = null)
        {
            return format == null
                ? number.ToString(CultureInfo.InvariantCulture)
                : number.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
