using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using CgfConverter;
using DDRIT = Dolkens.Framework.Extensions.ExtensionMethods;

namespace Dolkens.Framework.Extensions
{
    /// <summary>
    /// The MIT License (MIT)
    /// 
    /// Copyright (c) 2008 Peter Dolkens
    /// 
    /// Permission is hereby granted, free of charge, to any person obtaining a copy
    /// of this software and associated documentation files (the "Software"), to deal
    /// in the Software without restriction, including without limitation the rights
    /// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    /// copies of the Software, and to permit persons to whom the Software is
    /// furnished to do so, subject to the following conditions:
    /// 
    /// The above copyright notice and this permission notice shall be included in
    /// all copies or substantial portions of the Software.
    /// 
    /// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    /// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    /// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    /// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    /// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    /// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    /// THE SOFTWARE.
    /// </summary>
    public static class ExtensionMethods
    {
        #region Private Members

        private static readonly Dictionary<Type, MethodInfo> _parserMap = new();
        private static readonly Dictionary<Type, MethodInfo> _byteArrayMap = new();
        private static readonly Dictionary<Type, ConstructorInfo> _byteConstructorMap = new();

        #endregion

        #region Serialization

        #region XML

        /// <summary>
        /// Attempt to serialize any object into a XML string.
        /// </summary>
        /// <param name="input">The object to serialize.</param>
        /// <returns>A string containing the serialized object.</returns>
        public static string ToXML(this object input)
        {
            XmlSerializer serializer = new(input.GetType());

            using (MemoryStream ms = new())
            {
                XmlSerializerNamespaces ns = new();
                ns.Add("", "");

                using (var xmlTextWriter = new XmlTextWriter(ms, Encoding.UTF8))
                {
                    serializer.Serialize(xmlTextWriter, input, ns);
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <param name="type">The type to convert to.</param>
        /// <returns>An Object deserialized from the string.</returns>
        public static object FromXML(this string input, Type type)
        {
            XmlSerializer serializer = new(type);

            byte[] bytes = Encoding.UTF8.GetBytes(input);

            using (var stream = new MemoryStream(bytes))
            {
                return serializer.Deserialize(stream);
            }

        }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <typeparam name="TResult">The type to convert to.</typeparam>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <returns>An Object of type T deserialized from the string.</returns>
        public static TResult FromXML<TResult>(this string input)
        {
            object buffer = DDRIT.FromXML(input, typeof(TResult));

            return (buffer == null) ? default : (TResult)buffer;
        }

        #endregion

        #endregion

        #region Type Extensions

        private static Dictionary<Type, Attribute> _getAttributeLookup = null;
        public static TAttribute GetAttribute<TAttribute>(this Type type) where TAttribute : Attribute
        {
            if (_getAttributeLookup == null)
            {
                _getAttributeLookup = new Dictionary<Type, Attribute>();
            }

            if (!_getAttributeLookup.Keys.Contains(type))
            {
                TAttribute attr = type.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() as TAttribute;
                if (attr == null)
                    throw new InvalidOperationException($"Attribute {typeof(TAttribute).FullName} was not found on type {type.FullName}.");
                _getAttributeLookup[type] = type.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() as TAttribute;
            }

            return _getAttributeLookup[type] as TAttribute;
        }

        public static bool IsNullable(this Type baseType)
        {
            return baseType.IsGenericType && (baseType.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        public static string SafeToString(this object content)
        {
            if (content == null)
                return string.Empty;

            return content.ToString();
        }

        public static string GetFriendlyTypeName(this Type type)
        {
            StringBuilder sb = new();

            if (type.IsGenericType)
            {
                Type genericType = type.GetGenericTypeDefinition();

                sb.AppendFormat("{0}.{1}<", genericType.Namespace, genericType.Name);

                bool first = true;

                foreach (Type typeArgument in type.GetGenericArguments())
                {
                    if (!first)
                        sb.Append(",");

                    GetFriendlyTypeName(typeArgument, sb);

                    first = false;
                }

                sb.Append(">");
            }
            else
                sb.AppendFormat("{0}.{1}", type.Namespace, type.Name);

            return sb.ToString();
        }

        private static void GetFriendlyTypeName(Type type, StringBuilder sb)
        {
            if (type.IsGenericType)
            {
                Type genericType = type.GetGenericTypeDefinition();

                sb.AppendFormat("{0}.{1}<", genericType.Namespace, genericType.Name);

                bool first = true;

                foreach (Type typeArgument in type.GetGenericArguments())
                {
                    if (!first)
                        sb.Append(",");

                    GetFriendlyTypeName(typeArgument, sb);

                    first = false;
                }

                sb.Append(">");
            }
            else
                sb.AppendFormat("{0}.{1}", type.Namespace, type.Name);
        }

        #endregion

        #region Enum Extensions

        /// <summary>
        /// Return the friendly description of an enum value, if it has been decorated with the DescriptionAttribute,
        /// otherwise, return the internal string representation of the enum value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string ToDescription(this Enum value)
        {
            if (value == null)
                return string.Empty;

            string description = value.ToString();

            DescriptionAttribute[] attributes = (DescriptionAttribute[])value.GetType()
                .GetField(description)
                .GetCustomAttributes(typeof(DescriptionAttribute), false);

            return attributes.Length > 0 ? attributes[0].Description : description;
        }

        public static short? ToInt16(this Enum input)
        {
            if (input == null)
                return null;

            return (short)(object)input;
        }

        public static short ToInt16(this Enum input, short @default)
        {
            return input.ToInt16() ?? @default;
        }

        public static int? ToInt32(this Enum input)
        {
            if (input == null)
                return null;

            return (int)(object)input;
        }

        public static int ToInt32(this Enum input, int @default)
        {
            return input.ToInt32() ?? @default;
        }

        public static long? ToInt64(this Enum input)
        {
            if (input == null)
                return null;

            return (long)(object)input;
        }

        public static long ToInt64(this Enum input, long @default)
        {
            return input.ToInt64() ?? @default;
        }

        #endregion

        #region File.IO Extensions

        public static string FilePathToUri(this string path)
        {
            return path.Replace("\\", "/").Replace("./", "");
        }

        public static string GetRelativePath(this DirectoryInfo sourcePath, DirectoryInfo destinationPath)
        {
            StringBuilder path = new(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_DIRECTORY,
                destinationPath.FullName,
                FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static string GetRelativePath(this DirectoryInfo sourcePath, FileInfo destinationPath)
        {
            StringBuilder path = new(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_DIRECTORY,
                destinationPath.FullName,
                FILE_ATTRIBUTE_NORMAL) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static string GetRelativePath(this FileInfo sourcePath, DirectoryInfo destinationPath)
        {
            StringBuilder path = new(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_NORMAL,
                destinationPath.FullName,
                FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static string GetRelativePath(this FileInfo sourcePath, FileInfo destinationPath)
        {
            StringBuilder path = new(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_NORMAL,
                destinationPath.FullName,
                FILE_ATTRIBUTE_NORMAL) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shlwapi.dll", SetLastError = true)]
        private static extern int PathRelativePathTo(StringBuilder pszPath,
            string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo);

        #endregion

        #region Dictionary Extensions

        /// <summary>
        /// Attempts to convert any array of objects into a dictionary of objects.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Dictionary<object, object> ToDictionary(this object[] input)
        {
            int i = 0;
            Dictionary<object, object> buffer = new();

            if (input == null)
                return buffer;

            while (i < input.Length - 1)
                buffer[input[i++]] = input[i++];

            return buffer;
        }

        /// <summary>
        /// Converts an array of Object[] to a key/value pair dictionary.
        /// Duplicate keys will throw an exception.
        /// The last argument will be ignored if an odd number of arguments is supplied.
        /// </summary>
        /// <typeparam name="TKey">The type of the key parameters.</typeparam>
        /// <typeparam name="TValue">The type of the value parameters.</typeparam>
        /// <param name="args">The list of arguments to convert to a key/value pair dictionary</param>
        /// <returns>Returns a key/value pair dictionary</returns>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this object[] args)
        {
            int i = 0;
            Dictionary<TKey, TValue> buffer = new();

            if (args == null)
                return buffer;

            while (i < args.Length - 1)
                if ((args[i] is TKey) && (args[i + 1] is TValue))
                    buffer[(TKey)args[i++]] = (TValue)args[i++];
                else
                    i += 2;

            return buffer;
        }

        public static Dictionary<string, TValue> AddPrefix<TValue>(this Dictionary<string, TValue> input, string prefix)
        {
            return (from kvp in input
                    select new KeyValuePair<string, TValue>(string.Format("{0}{1}", prefix, kvp.Key), kvp.Value)).ToDictionary(k => k.Key, v => v.Value);
        }

        public static Dictionary<string, TValue> RemovePrefix<TValue>(this Dictionary<string, TValue> input, string prefix)
        {
            return (from kvp in input
                    where kvp.Key.StartsWith(prefix)
                    select new KeyValuePair<string, TValue>(kvp.Key.Substring(prefix.Length), kvp.Value)).ToDictionary(k => k.Key, v => v.Value);
        }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value)
        {
            input[key] = value;

            return input;
        }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, Dictionary<TKey, TValue> collection)
        {
            foreach (KeyValuePair<TKey, TValue> kvp in collection)
                input[kvp.Key] = kvp.Value;

            return input;
        }

        public static Dictionary<TKey, TValue> Prepend<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value)
        {
            Dictionary<TKey, TValue> buffer = new() { { key, value } };

            foreach (KeyValuePair<TKey, TValue> kvp in input)
                buffer[kvp.Key] = kvp.Value;

            return buffer;
        }

        public static TValue GetValue<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue @default)
        {
            return input.GetValueOrDefault(key, @default);
        }

        #endregion

        #region Stream Extensions

        /// <summary>
        /// Read a Length-prefixed string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="byteLength">Size of the Length representation</param>
        /// <returns></returns>
        public static string ReadPString(this BinaryReader binaryReader, StringSizeEnum byteLength = StringSizeEnum.Int32)
        {
            int stringLength = 0;

            switch (byteLength)
            {
                case StringSizeEnum.Int8:
                    stringLength = binaryReader.ReadByte();
                    break;
                case StringSizeEnum.Int16:
                    stringLength = binaryReader.ReadInt16();
                    break;
                case StringSizeEnum.Int32:
                    stringLength = binaryReader.ReadInt32();
                    break;
                default:
                    throw new NotSupportedException("Only Int8, Int16, and Int32 string sizes are supported");
            }

            // If there is actually a string to read
            if (stringLength > 0)
            {
                return new string(binaryReader.ReadChars(stringLength));
            }

            return null;
        }

        /// <summary>
        /// Read a NULL-Terminated string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <returns></returns>
        public static string ReadCString(this BinaryReader binaryReader)
        {
            var chars = new List<byte>();
            byte b = 255;
            while (b != 0 && binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
            {
                b = binaryReader.ReadByte();
                if (b != 0)
                {
                    chars.Add(b);
                }
            }
            return chars.Count > 0 ? new string(Encoding.UTF8.GetChars(chars.ToArray())) : null;
        }

        /// <summary>
        /// Read a Fixed-Length string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="stringLength">Size of the String</param>
        /// <returns></returns>
        public static string ReadFString(this BinaryReader binaryReader, int stringLength)
        {
            char[] chars = binaryReader.ReadChars(stringLength);

            for (int i = 0; i < stringLength; i++)
            {
                if (chars[i] == 0)
                {
                    return new string(chars, 0, i);
                }
            }

            return new string(chars);
        }

        public static byte[] ReadAllBytes(this Stream stream)
        {
            using (MemoryStream ms = new())
            {
                long oldPosition = stream.Position;
                stream.Position = 0;
                stream.CopyTo(ms);
                stream.Position = oldPosition;
                return ms.ToArray();
            }
        }

        #endregion

        #region Parser Extensions

        /// <summary>
        /// Converts a string into a target type.
        /// </summary>
        /// <param name="type">The type we wish to return.</param>
        /// <param name="input">The string value we wish to convert.</param>
        /// <returns>An object containing the data represented by the input string, in the input type.</returns>
        public static object Parse(this string input, Type type, object @default = null)
        {
            // Early exit for Strings
            if (type == typeof(string))
                return input;

            bool isNullable = false;

            // Add support for Nullable types
            if (type.IsNullable())
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                type = type.GetGenericArguments()[0];

                isNullable = true;
            }

            if (input == null)
                return @default;

            try
            {
                // Add some extra datetime support
                if (type == typeof(DateTime))
                {
                    DateTime result = DateTime.MinValue;

                    if (DateTime.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                        return result;
                    else
                        return DateTime.Parse(input);
                }

                // If it's an enumeration, we'll try this.
                if (type.IsEnum)
                    return Enum.Parse(type, input);

                // Throw in a little static reflection caching
                if (!DDRIT._parserMap.TryGetValue(type, out var parseMethod))
                {
                    // Attempt to find inbuilt parsing methods
                    parseMethod = type.GetMethod("Parse", new Type[] { typeof(string) });

                    DDRIT._parserMap[type] = parseMethod;
                }

                // If there's inbuilt parsing methods, attempt to use those.
                if (parseMethod != null)
                    return parseMethod.Invoke(null, new object[] { input });
            }
            catch (Exception)
            {
                if (!isNullable && (@default == null))
                    throw;

                return @default;
            }

            // And if we weren't clever enough to cast them ourselves, we'll die gracefully and let people know WTF happened.
            throw new NotImplementedException(string.Format("Unable to parse `{0}` types", type.FullName));
        }

        public static TResult To<TResult>(this string input, TResult @default = default)
        {
            Type type = typeof(TResult);

            return (TResult)DDRIT.Parse(input, type, default(TResult));
        }

        public static bool ToBoolean(this string input, bool @default)
        {
            return input.ToBoolean() ?? @default;
        }

        public static bool? ToBoolean(this string input)
        {
            if (bool.TryParse(input, out var result))
                return result;

            return null;
        }

        public static TEnum? ToEnum<TEnum>(this string input, bool ignoreCase = true) where TEnum : struct
        {
            if (!typeof(TEnum).IsEnum) throw new InvalidOperationException("Generic type argument is not a System.Enum");

            if (Enum.TryParse(input, ignoreCase, out TEnum buffer))
                return buffer;

            return null;
        }

        public static TResult ToEnum<TResult>(this string input, TResult @default, bool ignoreCase = true) where TResult : struct
        {
            return input.ToEnum<TResult>(ignoreCase) ?? @default;
        }

        public static short? ToInt16(this string input)
        {
            if (short.TryParse(input, out var result))
                return result;

            return null;
        }

        public static short ToInt16(this string input, short @default)
        {
            return input.ToInt16() ?? @default;
        }

        public static int? ToInt32(this string input)
        {
            if (int.TryParse(input, out var result))
                return result;

            return null;
        }

        public static int ToInt32(this string input, int @default)
        {
            return input.ToInt32() ?? @default;
        }

        public static long? ToInt64(this string input)
        {
            if (long.TryParse(input, out var result))
                return result;

            return null;
        }

        public static long ToInt64(this string input, long @default)
        {
            return input.ToInt64() ?? @default;
        }

        public static float? ToSingle(this string input)
        {
            if (float.TryParse(input, out var result))
                return result;

            return null;
        }

        public static float ToSingle(this string input, float @default)
        {
            return input.ToSingle() ?? @default;
        }

        public static double? ToDouble(this string input)
        {
            if (double.TryParse(input, out var result))
                return result;

            return null;
        }

        public static double ToDouble(this string input, double @default)
        {
            return input.ToDouble() ?? @default;
        }

        public static decimal? ToDecimal(this string input)
        {
            if (decimal.TryParse(input, out var result))
                return result;

            return null;
        }

        public static decimal ToDecimal(this string input, decimal @default)
        {
            return input.ToDecimal() ?? @default;
        }

        public static DateTime? ToDateTime(this string input)
        {
            DateTime result = DateTime.MinValue;

            if (DateTime.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                return result;

            if (DateTime.TryParse(input, out result))
                return result;

            return null;
        }

        public static DateTime ToDateTime(this string input, DateTime @default)
        {
            return input.ToDateTime() ?? @default;
        }

        public static DateTimeOffset? ToDateTimeOffset(this string input)
        {
            DateTimeOffset result = DateTimeOffset.MinValue;

            if (DateTimeOffset.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                return result;

            if (DateTimeOffset.TryParse(input, out result))
                return result;

            return null;
        }

        public static DateTimeOffset ToDateTimeOffset(this string input, DateTimeOffset @default)
        {
            return input.ToDateTime() ?? @default;
        }

        public static DateTimeOffset? ToDateTimeOffset(this DateTime? input)
        {
            if (input == null)
                return null;

            return new DateTimeOffset(input.Value);
        }

        public static DateTimeOffset ToDateTimeOffset(this DateTime input) { return new DateTimeOffset(input); }

        public static TimeSpan? ToTimeSpan(this string input)
        {
            TimeSpan result = TimeSpan.MinValue;

            if (TimeSpan.TryParse(input, out result))
                return result;

            return null;
        }

        public static TimeSpan ToTimeSpan(this string input, TimeSpan @default)
        {
            return input.ToTimeSpan() ?? @default;
        }

        #endregion

        #region String Extensions

        public static string StripTags(this string input)
        {
            char[] array = new char[input.Length];
            int arrayIndex = 0;
            bool inside = false;
            char let;

            for (int i = 0; i < input.Length; i++)
            {
                let = input[i];

                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }

            return new string(array, 0, arrayIndex);
        }

        public static string TrimTo(this string input, int maxLength, bool stripTags = true)
        {
            if (!string.IsNullOrWhiteSpace(input))
            {
                string cleanString = input.Replace(@"&nbsp;", " ").Replace("  ", " ");

                if (stripTags)
                    cleanString = cleanString.StripTags();

                if (cleanString.Length > 100)
                {
                    string[] parts = cleanString.Split(' ');
                    StringBuilder sb = new();
                    int i = 0;
                    int j = parts.Length;

                    while (i < j && (sb.Length + parts[i].Length < maxLength))
                    {
                        sb.Append(" ");
                        sb.Append(parts[i++]);
                    }

                    sb.Append("...");

                    return sb.ToString().Trim();
                }

                return cleanString;
            }

            return string.Empty;
        }

        #endregion

        #region Natural Language Extensions

        /// <summary>
        /// Converts an input integer to its ordinal number
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal</returns>
        public static string ToOrdinal(this int input)
        {
            return input + ToOrdinalSuffix(input);
        }

        /// <summary>
        /// Converts an input integer to its ordinal suffix
        /// Useful if you need to format the suffix separately of the number itself
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal suffix</returns>
        public static string ToOrdinalSuffix(this int input)
        {
            //TODO: this only handles English ordinals - in future we may wish to consider the culture

            //note, we are allowing zeroth - http://en.wikipedia.org/wiki/Zeroth
            if (input < 0)
                throw new ArgumentOutOfRangeException(nameof(input), "Ordinal numbers cannot be negative");

            //first check special case, if the result ends in 11, 12, 13, should be th
            switch (input % 100)
            {
                case 11:
                case 12:
                case 13:
                    return "th";
            }

            //else we just check the last digit
            switch (input % 10)
            {
                case 1:
                    return ("st");
                case 2:
                    return ("nd");
                case 3:
                    return ("rd");
                default:
                    return ("th");
            }
        }

        #endregion

        #region Cryptography Extensions

        /// <summary>
        /// From http://stackoverflow.com/questions/311165/how-do-you-convert-byte-array-to-hexadecimal-string-and-vice-versa/14333437#14333437
        /// </summary>
        /// <param name="bytes">Array of bytes to convert to hex string</param>
        /// <returns>A hex string representation of the input bytes</returns>
        public static string ToHex(this byte[] bytes)
        {
            char[] buffer = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                buffer[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                buffer[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
            }

            return new string(buffer);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An MD5 hash of the given string</returns>
        public static string ToMD5(this string input)
        {
            using MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA1 hash of the given string</returns>
        public static string ToSHA1(this string input)
        {
            using SHA1 sha = SHA1.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA256 hash of the given string</returns>
        public static string ToSHA256(this string input)
        {
            using SHA256 sha = SHA256.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA384 hash of the given string</returns>
        public static string ToSHA384(this string input)
        {
            using SHA384 sha = SHA384.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA512 hash of the given string</returns>
        public static string ToSHA512(this string input)
        {
            using SHA512 sha = SHA512.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        #endregion

        #region Enumerable Extensions

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default)
        {
            return source.Any() ? source.First() : @default;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource @default)
        {
            source = source.Where(predicate);
            return source.Any() ? source.First() : @default;
        }

        #endregion

        #region DateTime Extensions

        public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return date.Date.AddDays(-(int)date.AddDays(-(int)startOfWeek).DayOfWeek); }

        public static DateTime EndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return date.StartOfWeek(startOfWeek).AddDays(7); }

        public static DateTime TrimTo(this DateTime datetime, TimeSpan interval)
        {
            if (interval.TotalHours > 24)
                throw new ArgumentOutOfRangeException(nameof(interval), "Timespan too large, timespan must be less than 24 hours");

            return datetime.AddMilliseconds(0 - (datetime.TimeOfDay.TotalMilliseconds % interval.TotalMilliseconds));
        }

        public static DateTimeOffset TrimTo(this DateTimeOffset datetime, TimeSpan interval)
        {
            if (interval.TotalHours > 24)
                throw new ArgumentOutOfRangeException(nameof(interval), "Timespan too large, timespan must be less than 24 hours");

            return datetime.AddMilliseconds(0 - (datetime.TimeOfDay.TotalMilliseconds % interval.TotalMilliseconds));
        }

        public static TimeSpan TrimTo(this TimeSpan timespan, TimeSpan interval)
        {
            return TimeSpan.FromMilliseconds(timespan.TotalMilliseconds - (timespan.TotalMilliseconds % interval.TotalMilliseconds));
        }

        #endregion
    }
}

#region Namespace Proxies

namespace System
{
    public static class _Proxy
    {
        /// <summary>
        /// Attempt to serialize any object into a XML string.
        /// </summary>
        /// <param name="input">The object to serialize.</param>
        /// <returns>A string containing the serialized object.</returns>
        public static string ToXML(this object input) { return DDRIT.ToXML(input); }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <param name="type">The type to convert to.</param>
        /// <returns>An Object deserialized from the string.</returns>
        public static object FromXML(this string input, Type type) { return DDRIT.FromXML(input, type); }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <typeparam name="TResult">The type to convert to.</typeparam>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <returns>An Object of type T deserialized from the string.</returns>
        public static TResult FromXML<TResult>(this string input) { return DDRIT.FromXML<TResult>(input); }

        public static string ToHex(this byte[] bytes) { return DDRIT.ToHex(bytes); }

        /// <summary>
        /// Strip HTML/XML tags from any string.
        /// </summary>
        /// <param name="input">HTML content.</param>
        /// <returns>Content free of HTML tags.</returns>
        public static string StripTags(this string input) { return DDRIT.StripTags(input); }

        /// <summary>
        /// Trim markup to a particular length, and append an elipsis if there is truncated content
        /// </summary>
        /// <param name="input">The content to trim</param>
        /// <param name="maxLength">The maximum length of the returned content</param>
        /// <param name="stripTags">(true)Remove HTML tags before trimming</param>
        /// <returns></returns>
        public static string TrimTo(this string input, int maxLength, bool stripTags = true) { return DDRIT.TrimTo(input, maxLength, stripTags); }

        /// <summary>
        /// Trim a DateTime to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static DateTime TrimTo(this DateTime input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        /// <summary>
        /// Trim a DateTime to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static DateTimeOffset TrimTo(this DateTimeOffset input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        /// <summary>
        /// Trim a TimeSpan to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static TimeSpan TrimTo(this TimeSpan input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        public static bool IsNullable(this Type baseType) { return DDRIT.IsNullable(baseType); }

        public static TAttribute GetAttribute<TAttribute>(this Type type) where TAttribute : Attribute { return DDRIT.GetAttribute<TAttribute>(type); }

        public static string GetFriendlyTypeName(this Type type) { return DDRIT.GetFriendlyTypeName(type); }

        /// <summary>
        /// Return the friendly description of an enum value, if it has been decorated with the DescriptionAttribute,
        /// otherwise, return the internal string representation of the enum value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string ToDescription(this Enum value) { return DDRIT.ToDescription(value); }

        public static string FilePathToUri(this string path) { return DDRIT.FilePathToUri(path); }

        public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return DDRIT.StartOfWeek(date, startOfWeek); }

        public static DateTime EndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return DDRIT.EndOfWeek(date, startOfWeek); }

        /// <summary>
        /// Attempts to convert any array of objects into a dictionary of objects.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Dictionary<object, object> ToDictionary(this object[] input) { return DDRIT.ToDictionary(input); }

        /// <summary>
        /// Converts an array of Object[] to a key/value pair dictionary.
        /// Duplicate keys will throw an exception.
        /// The last argument will be ignored if an odd number of arguments is supplied.
        /// </summary>
        /// <typeparam name="TKey">The type of the key parameters.</typeparam>
        /// <typeparam name="TValue">The type of the value parameters.</typeparam>
        /// <param name="args">The list of arguments to convert to a key/value pair dictionary</param>
        /// <returns>Returns a key/value pair dictionary</returns>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this object[] args) { return DDRIT.ToDictionary<TKey, TValue>(args); }

        /// <summary>
        /// Read a Length-prefixed string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="byteLength">Size of the Length representation</param>
        /// <returns></returns>
        public static string ReadPString(this BinaryReader binaryReader, StringSizeEnum byteLength = StringSizeEnum.Int32) { return DDRIT.ReadPString(binaryReader, byteLength); }

        /// <summary>
        /// Read a NULL-Terminated string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <returns></returns>
        public static string ReadCString(this BinaryReader binaryReader) { return DDRIT.ReadCString(binaryReader); }

        /// <summary>
        /// Read a Fixed-Length string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="stringLength">Size of the String</param>
        /// <returns></returns>
        public static string ReadFString(this BinaryReader binaryReader, int stringLength) { return DDRIT.ReadFString(binaryReader, stringLength); }

        public static byte[] ReadAllBytes(this Stream stream) { return DDRIT.ReadAllBytes(stream); }

        /// <summary>
        /// Converts a string into a target type.
        /// </summary>
        /// <param name="type">The type we wish to return.</param>
        /// <param name="input">The string value we wish to convert.</param>
        /// <returns>An object containing the data represented by the input string, in the input type.</returns>
        public static object Parse(this string input, Type type, object @default = null) { return DDRIT.Parse(input, type, @default); }

        public static TResult To<TResult>(this string input, TResult @default = default) { return DDRIT.To(input, @default); }

        public static bool? ToBoolean(this string input) { return DDRIT.ToBoolean(input); }

        public static bool ToBoolean(this string input, bool @default) { return DDRIT.ToBoolean(input, @default); }

        public static short? ToInt16(this string input) { return DDRIT.ToInt16(input); }

        public static short ToInt16(this string input, short @default) { return DDRIT.ToInt16(input, @default); }

        public static int? ToInt32(this string input) { return DDRIT.ToInt32(input); }

        public static int ToInt32(this string input, int @default) { return DDRIT.ToInt32(input, @default); }

        public static long? ToInt64(this string input) { return DDRIT.ToInt64(input); }

        public static long ToInt64(this string input, long @default) { return DDRIT.ToInt64(input, @default); }

        public static short? ToInt16(this Enum input) { return DDRIT.ToInt16(input); }

        public static short ToInt16(this Enum input, short @default) { return DDRIT.ToInt16(input, @default); }

        public static int? ToInt32(this Enum input) { return DDRIT.ToInt32(input); }

        public static int ToInt32(this Enum input, int @default) { return DDRIT.ToInt32(input, @default); }

        public static long? ToInt64(this Enum input) { return DDRIT.ToInt64(input); }

        public static long ToInt64(this Enum input, long @default) { return DDRIT.ToInt64(input, @default); }

        public static TResult ToEnum<TResult>(this string input, TResult @default, bool ignoreCase = true) where TResult : struct { return DDRIT.ToEnum(input, @default, ignoreCase); }

        public static TEnum? ToEnum<TEnum>(this string input, bool ignoreCase = true) where TEnum : struct { return DDRIT.ToEnum<TEnum>(input, ignoreCase); }

        public static float? ToSingle(this string input) { return DDRIT.ToSingle(input); }

        public static float ToSingle(this string input, float @default) { return DDRIT.ToSingle(input, @default); }

        public static double? ToDouble(this string input) { return DDRIT.ToDouble(input); }

        public static double ToDouble(this string input, double @default) { return DDRIT.ToDouble(input, @default); }

        public static decimal? ToDecimal(this string input) { return DDRIT.ToDecimal(input); }

        public static decimal ToDecimal(this string input, decimal @default) { return DDRIT.ToDecimal(input, @default); }

        public static DateTime? ToDateTime(this string input) { return DDRIT.ToDateTime(input); }

        public static DateTime ToDateTime(this string input, DateTime @default) { return DDRIT.ToDateTime(input, @default); }

        public static DateTimeOffset? ToDateTimeOffset(this DateTime? input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset ToDateTimeOffset(this DateTime input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset? ToDateTimeOffset(this string input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset ToDateTimeOffset(this string input, DateTime @default) { return DDRIT.ToDateTimeOffset(input, @default); }

        public static TimeSpan? ToTimeSpan(this string input) { return DDRIT.ToTimeSpan(input); }

        public static TimeSpan ToTimeSpan(this string input, TimeSpan @default) { return DDRIT.ToTimeSpan(input, @default); }

        /// <summary>
        /// Converts an input integer to its ordinal number
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal</returns>
        public static string ToOrdinal(this int input) { return DDRIT.ToOrdinal(input); }

        /// <summary>
        /// Converts an input integer to its ordinal suffix
        /// Useful if you need to format the suffix separately of the number itself
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal suffix</returns>
        public static string ToOrdinalSuffix(this int input) { return DDRIT.ToOrdinalSuffix(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An MD5 hash of the given string</returns>
        public static string ToMD5(this string input) { return DDRIT.ToMD5(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA1 hash of the given string</returns>
        public static string ToSHA1(this string input) { return DDRIT.ToSHA1(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA256 hash of the given string</returns>
        public static string ToSHA256(this string input) { return DDRIT.ToSHA256(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA384 hash of the given string</returns>
        public static string ToSHA384(this string input) { return DDRIT.ToSHA384(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA512 hash of the given string</returns>
        public static string ToSHA512(this string input) { return DDRIT.ToSHA512(input); }
    }
}

namespace System.Linq
{
    public static class _Proxy
    {
        //
        // Summary:
        //     Returns the first element of a sequence, or a default value if the sequence
        //     contains no elements.
        //
        // Parameters:
        //   source:
        //     The System.Collections.Generic.IEnumerable<T> to return the first element
        //     of.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        // Returns:
        //     default(TSource) if source is empty; otherwise, the first element in source.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source is null.
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default)
        {
            return DDRIT.FirstOrDefault(source, @default);
        }

        //
        // Summary:
        //     Returns the first element of the sequence that satisfies a condition or a
        //     default value if no such element is found.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to return an element from.
        //
        //   predicate:
        //     A function to test each element for a condition.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        // Returns:
        //     default(TSource) if source is empty or if no element passes the test specified
        //     by predicate; otherwise, the first element in source that passes the test
        //     specified by predicate.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or predicate is null.
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource @default)
        {
            return DDRIT.FirstOrDefault(source, predicate, @default);
        }
    }
}

namespace System.IO
{
    public static class _Proxy
    {
        public static string GetRelativePath(this DirectoryInfo sourcePath, DirectoryInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static string GetRelativePath(this DirectoryInfo sourcePath, FileInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static string GetRelativePath(this FileInfo sourcePath, DirectoryInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static string GetRelativePath(this FileInfo sourcePath, FileInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }
    }
}

namespace System.Collections.Generic
{
    public static class _Proxy
    {
        public static Dictionary<string, TValue> AddPrefix<TValue>(this Dictionary<string, TValue> input, string prefix) { return DDRIT.AddPrefix(input, prefix); }

        public static Dictionary<string, TValue> RemovePrefix<TValue>(this Dictionary<string, TValue> input, string prefix) { return DDRIT.RemovePrefix(input, prefix); }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value) { return DDRIT.Append(input, key, value); }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, Dictionary<TKey, TValue> collection) { return DDRIT.Append(input, collection); }

        public static Dictionary<TKey, TValue> Prepend<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value) { return DDRIT.Prepend(input, key, value); }

        public static TValue GetValue<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue @default) { return DDRIT.GetValue(input, key, @default); }
    }
}

#endregion
