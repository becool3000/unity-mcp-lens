using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

#nullable disable
namespace HuggingfaceHub.Utilities
{
    class HttpEncoder
    {
        static char[] hexChars = "0123456789abcdef".ToCharArray();
        static object entitiesLock = new object();
        static SortedDictionary<string, char> entities;
        static HttpEncoder defaultEncoder = new HttpEncoder();
        static HttpEncoder currentEncoder = defaultEncoder;

        static IDictionary<string, char> Entities
        {
            get
            {
                lock (entitiesLock)
                {
                    if (entities == null)
                        InitEntities();
                    return (IDictionary<string, char>)entities;
                }
            }
        }

        public static HttpEncoder Current
        {
            get => currentEncoder;
            set { currentEncoder = value != null ? value : throw new ArgumentNullException(nameof(value)); }
        }

        public static HttpEncoder Default => defaultEncoder;

        protected internal virtual void HeaderNameValueEncode(
            string headerName,
            string headerValue,
            out string encodedHeaderName,
            out string encodedHeaderValue)
        {
            encodedHeaderName = !string.IsNullOrEmpty(headerName)
                ? EncodeHeaderString(headerName)
                : headerName;
            if (string.IsNullOrEmpty(headerValue))
                encodedHeaderValue = headerValue;
            else
                encodedHeaderValue = EncodeHeaderString(headerValue);
        }

        static void StringBuilderAppend(string s, ref StringBuilder sb)
        {
            if (sb == null)
                sb = new StringBuilder(s);
            else
                sb.Append(s);
        }

        static string EncodeHeaderString(string input)
        {
            StringBuilder sb = (StringBuilder)null;
            for (int index = 0; index < input.Length; ++index)
            {
                char ch = input[index];
                if (ch < ' ' && ch != '\t' || ch == '\u007F')
                    StringBuilderAppend($"%{(int)ch:x2}", ref sb);
            }

            return sb != null ? sb.ToString() : input;
        }

        protected internal virtual void HtmlAttributeEncode(string value, TextWriter output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (string.IsNullOrEmpty(value))
                return;
            output.Write(HtmlAttributeEncode(value));
        }

        protected internal virtual void HtmlDecode(string value, TextWriter output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            output.Write(HtmlDecode(value));
        }

        protected internal virtual void HtmlEncode(string value, TextWriter output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            output.Write(HtmlEncode(value));
        }

        protected internal virtual byte[] UrlEncode(byte[] bytes, int offset, int count)
        {
            return UrlEncodeToBytes(bytes, offset, count);
        }

        protected internal virtual string UrlPathEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            MemoryStream result = new MemoryStream();
            int length = value.Length;
            for (int index = 0; index < length; ++index)
                UrlPathEncodeChar(value[index], (Stream)result);
            return Encoding.ASCII.GetString(result.ToArray());
        }

        internal static byte[] UrlEncodeToBytes(byte[] bytes, int offset, int count)
        {
            int num1 = bytes != null ? bytes.Length : throw new ArgumentNullException(nameof(bytes));
            if (num1 == 0)
                return new byte[0];
            if (offset < 0 || offset >= num1)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > num1 - offset)
                throw new ArgumentOutOfRangeException(nameof(count));
            MemoryStream result = new MemoryStream(count);
            int num2 = offset + count;
            for (int index = offset; index < num2; ++index)
                UrlEncodeChar((char)bytes[index], (Stream)result, false);
            return result.ToArray();
        }

        internal static string HtmlEncode(string s)
        {
            switch (s)
            {
                case null:
                    return (string)null;
                case "":
                    return string.Empty;
                default:
                    bool flag = false;
                    for (int index = 0; index < s.Length; ++index)
                    {
                        char ch = s[index];
                        if (ch == '&' || ch == '"' || ch == '<' || ch == '>' || ch > '\u009F' || ch == '\'')
                        {
                            flag = true;
                            break;
                        }
                    }

                    if (!flag)
                        return s;
                    StringBuilder stringBuilder = new StringBuilder();
                    int length = s.Length;
                    for (int index = 0; index < length; ++index)
                    {
                        char ch = s[index];
                        switch (ch)
                        {
                            case '"':
                                stringBuilder.Append("&quot;");
                                break;
                            case '&':
                                stringBuilder.Append("&amp;");
                                break;
                            case '\'':
                                stringBuilder.Append("&#39;");
                                break;
                            case '<':
                                stringBuilder.Append("&lt;");
                                break;
                            case '>':
                                stringBuilder.Append("&gt;");
                                break;
                            case '＜':
                                stringBuilder.Append("&#65308;");
                                break;
                            case '＞':
                                stringBuilder.Append("&#65310;");
                                break;
                            default:
                                if (ch > '\u009F' && ch < 'Ā')
                                {
                                    stringBuilder.Append("&#");
                                    stringBuilder.Append(
                                        ((int)ch).ToString((IFormatProvider)CultureInfo.InvariantCulture));
                                    stringBuilder.Append(";");
                                    break;
                                }

                                stringBuilder.Append(ch);
                                break;
                        }
                    }

                    return stringBuilder.ToString();
            }
        }

        internal static string HtmlAttributeEncode(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            bool flag = false;
            for (int index = 0; index < s.Length; ++index)
            {
                char ch = s[index];
                int num;
                switch (ch)
                {
                    case '"':
                    case '&':
                    case '<':
                        num = 0;
                        break;
                    default:
                        num = ch != '\'' ? 1 : 0;
                        break;
                }

                if (num == 0)
                {
                    flag = true;
                    break;
                }
            }

            if (!flag)
                return s;
            StringBuilder stringBuilder = new StringBuilder();
            int length = s.Length;
            for (int index = 0; index < length; ++index)
            {
                char ch = s[index];
                switch (ch)
                {
                    case '"':
                        stringBuilder.Append("&quot;");
                        break;
                    case '&':
                        stringBuilder.Append("&amp;");
                        break;
                    case '\'':
                        stringBuilder.Append("&#39;");
                        break;
                    case '<':
                        stringBuilder.Append("&lt;");
                        break;
                    default:
                        stringBuilder.Append(ch);
                        break;
                }
            }

            return stringBuilder.ToString();
        }

        internal static string HtmlDecode(string s)
        {
            switch (s)
            {
                case null:
                    return (string)null;
                case "":
                    return string.Empty;
                default:
                    if (s.IndexOf('&') == -1)
                        return s;
                    StringBuilder stringBuilder1 = new StringBuilder();
                    StringBuilder stringBuilder2 = new StringBuilder();
                    StringBuilder stringBuilder3 = new StringBuilder();
                    int length = s.Length;
                    int num1 = 0;
                    int num2 = 0;
                    bool flag1 = false;
                    bool flag2 = false;
                    for (int index = 0; index < length; ++index)
                    {
                        char ch = s[index];
                        if (num1 == 0)
                        {
                            if (ch == '&')
                            {
                                stringBuilder2.Append(ch);
                                stringBuilder1.Append(ch);
                                num1 = 1;
                            }
                            else
                                stringBuilder3.Append(ch);
                        }
                        else if (ch == '&')
                        {
                            num1 = 1;
                            if (flag2)
                            {
                                stringBuilder2.Append(num2.ToString((IFormatProvider)CultureInfo.InvariantCulture));
                                flag2 = false;
                            }

                            stringBuilder3.Append(stringBuilder2.ToString());
                            stringBuilder2.Length = 0;
                            stringBuilder2.Append('&');
                        }
                        else
                        {
                            switch (num1)
                            {
                                case 1:
                                    if (ch == ';')
                                    {
                                        num1 = 0;
                                        stringBuilder3.Append(stringBuilder2.ToString());
                                        stringBuilder3.Append(ch);
                                        stringBuilder2.Length = 0;
                                        break;
                                    }

                                    num2 = 0;
                                    flag1 = false;
                                    num1 = ch == '#' ? 3 : 2;
                                    stringBuilder2.Append(ch);
                                    stringBuilder1.Append(ch);
                                    break;
                                case 2:
                                    stringBuilder2.Append(ch);
                                    if (ch == ';')
                                    {
                                        string str = stringBuilder2.ToString();
                                        if (str.Length > 1 &&
                                            Entities.ContainsKey(str.Substring(1, str.Length - 2)))
                                            str = Entities[str.Substring(1, str.Length - 2)].ToString();
                                        stringBuilder3.Append(str);
                                        num1 = 0;
                                        stringBuilder2.Length = 0;
                                        stringBuilder1.Length = 0;
                                        break;
                                    }

                                    break;
                                case 3:
                                    if (ch == ';')
                                    {
                                        if (num2 == 0)
                                            stringBuilder3.Append(stringBuilder1.ToString() + ";");
                                        else if (num2 > (int)ushort.MaxValue)
                                        {
                                            stringBuilder3.Append("&#");
                                            stringBuilder3.Append(
                                                num2.ToString((IFormatProvider)CultureInfo.InvariantCulture));
                                            stringBuilder3.Append(";");
                                        }
                                        else
                                            stringBuilder3.Append((char)num2);

                                        num1 = 0;
                                        stringBuilder2.Length = 0;
                                        stringBuilder1.Length = 0;
                                        flag2 = false;
                                    }
                                    else if (flag1 && Uri.IsHexDigit(ch))
                                    {
                                        num2 = num2 * 16 /*0x10*/ + Uri.FromHex(ch);
                                        flag2 = true;
                                        stringBuilder1.Append(ch);
                                    }
                                    else if (char.IsDigit(ch))
                                    {
                                        num2 = num2 * 10 + ((int)ch - 48 /*0x30*/);
                                        flag2 = true;
                                        stringBuilder1.Append(ch);
                                    }
                                    else if (num2 == 0 && (ch == 'x' || ch == 'X'))
                                    {
                                        flag1 = true;
                                        stringBuilder1.Append(ch);
                                    }
                                    else
                                    {
                                        num1 = 2;
                                        if (flag2)
                                        {
                                            stringBuilder2.Append(
                                                num2.ToString((IFormatProvider)CultureInfo.InvariantCulture));
                                            flag2 = false;
                                        }

                                        stringBuilder2.Append(ch);
                                    }

                                    break;
                            }
                        }
                    }

                    if (stringBuilder2.Length > 0)
                        stringBuilder3.Append(stringBuilder2.ToString());
                    else if (flag2)
                        stringBuilder3.Append(num2.ToString((IFormatProvider)CultureInfo.InvariantCulture));
                    return stringBuilder3.ToString();
            }
        }

        internal static bool NotEncoded(char c)
        {
            return c == '!' || c == '(' || c == ')' || c == '*' || c == '-' || c == '.' || c == '_';
        }

        internal static void UrlEncodeChar(char c, Stream result, bool isUnicode)
        {
            if (c > 'ÿ')
            {
                int num = (int)c;
                result.WriteByte((byte)37);
                result.WriteByte((byte)117);
                int index1 = num >> 12;
                result.WriteByte((byte)hexChars[index1]);
                int index2 = num >> 8 & 15;
                result.WriteByte((byte)hexChars[index2]);
                int index3 = num >> 4 & 15;
                result.WriteByte((byte)hexChars[index3]);
                int index4 = num & 15;
                result.WriteByte((byte)hexChars[index4]);
            }
            else if (c > ' ' && NotEncoded(c))
                result.WriteByte((byte)c);
            else if (c == ' ')
                result.WriteByte((byte)43);
            else if (c < '0' || c < 'A' && c > '9' || c > 'Z' && c < 'a' || c > 'z')
            {
                if (isUnicode && c > '\u007F')
                {
                    result.WriteByte((byte)37);
                    result.WriteByte((byte)117);
                    result.WriteByte((byte)48 /*0x30*/);
                    result.WriteByte((byte)48 /*0x30*/);
                }
                else
                    result.WriteByte((byte)37);

                int index5 = (int)c >> 4;
                result.WriteByte((byte)hexChars[index5]);
                int index6 = (int)c & 15;
                result.WriteByte((byte)hexChars[index6]);
            }
            else
                result.WriteByte((byte)c);
        }

        internal static void UrlPathEncodeChar(char c, Stream result)
        {
            if (c < '!' || c > '~')
            {
                byte[] bytes = Encoding.UTF8.GetBytes(c.ToString());
                for (int index1 = 0; index1 < bytes.Length; ++index1)
                {
                    result.WriteByte((byte)37);
                    int index2 = (int)bytes[index1] >> 4;
                    result.WriteByte((byte)hexChars[index2]);
                    int index3 = (int)bytes[index1] & 15;
                    result.WriteByte((byte)hexChars[index3]);
                }
            }
            else if (c == ' ')
            {
                result.WriteByte((byte)37);
                result.WriteByte((byte)50);
                result.WriteByte((byte)48 /*0x30*/);
            }
            else
                result.WriteByte((byte)c);
        }

        static void InitEntities()
        {
            entities = new SortedDictionary<string, char>((IComparer<string>)StringComparer.Ordinal);
            entities.Add("nbsp", ' ');
            entities.Add("iexcl", '¡');
            entities.Add("cent", '¢');
            entities.Add("pound", '£');
            entities.Add("curren", '¤');
            entities.Add("yen", '¥');
            entities.Add("brvbar", '¦');
            entities.Add("sect", '§');
            entities.Add("uml", '¨');
            entities.Add("copy", '©');
            entities.Add("ordf", 'ª');
            entities.Add("laquo", '«');
            entities.Add("not", '¬');
            entities.Add("shy", '\u00AD');
            entities.Add("reg", '®');
            entities.Add("macr", '¯');
            entities.Add("deg", '°');
            entities.Add("plusmn", '±');
            entities.Add("sup2", '\u00B2');
            entities.Add("sup3", '\u00B3');
            entities.Add("acute", '´');
            entities.Add("micro", 'µ');
            entities.Add("para", '¶');
            entities.Add("middot", '·');
            entities.Add("cedil", '¸');
            entities.Add("sup1", '\u00B9');
            entities.Add("ordm", 'º');
            entities.Add("raquo", '»');
            entities.Add("frac14", '\u00BC');
            entities.Add("frac12", '\u00BD');
            entities.Add("frac34", '\u00BE');
            entities.Add("iquest", '¿');
            entities.Add("Agrave", 'À');
            entities.Add("Aacute", 'Á');
            entities.Add("Acirc", 'Â');
            entities.Add("Atilde", 'Ã');
            entities.Add("Auml", 'Ä');
            entities.Add("Aring", 'Å');
            entities.Add("AElig", 'Æ');
            entities.Add("Ccedil", 'Ç');
            entities.Add("Egrave", 'È');
            entities.Add("Eacute", 'É');
            entities.Add("Ecirc", 'Ê');
            entities.Add("Euml", 'Ë');
            entities.Add("Igrave", 'Ì');
            entities.Add("Iacute", 'Í');
            entities.Add("Icirc", 'Î');
            entities.Add("Iuml", 'Ï');
            entities.Add("ETH", 'Ð');
            entities.Add("Ntilde", 'Ñ');
            entities.Add("Ograve", 'Ò');
            entities.Add("Oacute", 'Ó');
            entities.Add("Ocirc", 'Ô');
            entities.Add("Otilde", 'Õ');
            entities.Add("Ouml", 'Ö');
            entities.Add("times", '×');
            entities.Add("Oslash", 'Ø');
            entities.Add("Ugrave", 'Ù');
            entities.Add("Uacute", 'Ú');
            entities.Add("Ucirc", 'Û');
            entities.Add("Uuml", 'Ü');
            entities.Add("Yacute", 'Ý');
            entities.Add("THORN", 'Þ');
            entities.Add("szlig", 'ß');
            entities.Add("agrave", 'à');
            entities.Add("aacute", 'á');
            entities.Add("acirc", 'â');
            entities.Add("atilde", 'ã');
            entities.Add("auml", 'ä');
            entities.Add("aring", 'å');
            entities.Add("aelig", 'æ');
            entities.Add("ccedil", 'ç');
            entities.Add("egrave", 'è');
            entities.Add("eacute", 'é');
            entities.Add("ecirc", 'ê');
            entities.Add("euml", 'ë');
            entities.Add("igrave", 'ì');
            entities.Add("iacute", 'í');
            entities.Add("icirc", 'î');
            entities.Add("iuml", 'ï');
            entities.Add("eth", 'ð');
            entities.Add("ntilde", 'ñ');
            entities.Add("ograve", 'ò');
            entities.Add("oacute", 'ó');
            entities.Add("ocirc", 'ô');
            entities.Add("otilde", 'õ');
            entities.Add("ouml", 'ö');
            entities.Add("divide", '÷');
            entities.Add("oslash", 'ø');
            entities.Add("ugrave", 'ù');
            entities.Add("uacute", 'ú');
            entities.Add("ucirc", 'û');
            entities.Add("uuml", 'ü');
            entities.Add("yacute", 'ý');
            entities.Add("thorn", 'þ');
            entities.Add("yuml", 'ÿ');
            entities.Add("fnof", 'ƒ');
            entities.Add("Alpha", 'Α');
            entities.Add("Beta", 'Β');
            entities.Add("Gamma", 'Γ');
            entities.Add("Delta", 'Δ');
            entities.Add("Epsilon", 'Ε');
            entities.Add("Zeta", 'Ζ');
            entities.Add("Eta", 'Η');
            entities.Add("Theta", 'Θ');
            entities.Add("Iota", 'Ι');
            entities.Add("Kappa", 'Κ');
            entities.Add("Lambda", 'Λ');
            entities.Add("Mu", 'Μ');
            entities.Add("Nu", 'Ν');
            entities.Add("Xi", 'Ξ');
            entities.Add("Omicron", 'Ο');
            entities.Add("Pi", 'Π');
            entities.Add("Rho", 'Ρ');
            entities.Add("Sigma", 'Σ');
            entities.Add("Tau", 'Τ');
            entities.Add("Upsilon", 'Υ');
            entities.Add("Phi", 'Φ');
            entities.Add("Chi", 'Χ');
            entities.Add("Psi", 'Ψ');
            entities.Add("Omega", 'Ω');
            entities.Add("alpha", 'α');
            entities.Add("beta", 'β');
            entities.Add("gamma", 'γ');
            entities.Add("delta", 'δ');
            entities.Add("epsilon", 'ε');
            entities.Add("zeta", 'ζ');
            entities.Add("eta", 'η');
            entities.Add("theta", 'θ');
            entities.Add("iota", 'ι');
            entities.Add("kappa", 'κ');
            entities.Add("lambda", 'λ');
            entities.Add("mu", 'μ');
            entities.Add("nu", 'ν');
            entities.Add("xi", 'ξ');
            entities.Add("omicron", 'ο');
            entities.Add("pi", 'π');
            entities.Add("rho", 'ρ');
            entities.Add("sigmaf", 'ς');
            entities.Add("sigma", 'σ');
            entities.Add("tau", 'τ');
            entities.Add("upsilon", 'υ');
            entities.Add("phi", 'φ');
            entities.Add("chi", 'χ');
            entities.Add("psi", 'ψ');
            entities.Add("omega", 'ω');
            entities.Add("thetasym", 'ϑ');
            entities.Add("upsih", 'ϒ');
            entities.Add("piv", 'ϖ');
            entities.Add("bull", '•');
            entities.Add("hellip", '…');
            entities.Add("prime", '′');
            entities.Add("Prime", '″');
            entities.Add("oline", '‾');
            entities.Add("frasl", '⁄');
            entities.Add("weierp", '℘');
            entities.Add("image", 'ℑ');
            entities.Add("real", 'ℜ');
            entities.Add("trade", '™');
            entities.Add("alefsym", 'ℵ');
            entities.Add("larr", '←');
            entities.Add("uarr", '↑');
            entities.Add("rarr", '→');
            entities.Add("darr", '↓');
            entities.Add("harr", '↔');
            entities.Add("crarr", '↵');
            entities.Add("lArr", '⇐');
            entities.Add("uArr", '⇑');
            entities.Add("rArr", '⇒');
            entities.Add("dArr", '⇓');
            entities.Add("hArr", '⇔');
            entities.Add("forall", '∀');
            entities.Add("part", '∂');
            entities.Add("exist", '∃');
            entities.Add("empty", '∅');
            entities.Add("nabla", '∇');
            entities.Add("isin", '∈');
            entities.Add("notin", '∉');
            entities.Add("ni", '∋');
            entities.Add("prod", '∏');
            entities.Add("sum", '∑');
            entities.Add("minus", '−');
            entities.Add("lowast", '∗');
            entities.Add("radic", '√');
            entities.Add("prop", '∝');
            entities.Add("infin", '∞');
            entities.Add("ang", '∠');
            entities.Add("and", '∧');
            entities.Add("or", '∨');
            entities.Add("cap", '∩');
            entities.Add("cup", '∪');
            entities.Add("int", '∫');
            entities.Add("there4", '∴');
            entities.Add("sim", '∼');
            entities.Add("cong", '≅');
            entities.Add("asymp", '≈');
            entities.Add("ne", '≠');
            entities.Add("equiv", '≡');
            entities.Add("le", '≤');
            entities.Add("ge", '≥');
            entities.Add("sub", '⊂');
            entities.Add("sup", '⊃');
            entities.Add("nsub", '⊄');
            entities.Add("sube", '⊆');
            entities.Add("supe", '⊇');
            entities.Add("oplus", '⊕');
            entities.Add("otimes", '⊗');
            entities.Add("perp", '⊥');
            entities.Add("sdot", '⋅');
            entities.Add("lceil", '⌈');
            entities.Add("rceil", '⌉');
            entities.Add("lfloor", '⌊');
            entities.Add("rfloor", '⌋');
            entities.Add("lang", '〈');
            entities.Add("rang", '〉');
            entities.Add("loz", '◊');
            entities.Add("spades", '♠');
            entities.Add("clubs", '♣');
            entities.Add("hearts", '♥');
            entities.Add("diams", '♦');
            entities.Add("quot", '"');
            entities.Add("amp", '&');
            entities.Add("lt", '<');
            entities.Add("gt", '>');
            entities.Add("OElig", 'Œ');
            entities.Add("oelig", 'œ');
            entities.Add("Scaron", 'Š');
            entities.Add("scaron", 'š');
            entities.Add("Yuml", 'Ÿ');
            entities.Add("circ", 'ˆ');
            entities.Add("tilde", '˜');
            entities.Add("ensp", ' ');
            entities.Add("emsp", ' ');
            entities.Add("thinsp", ' ');
            entities.Add("zwnj", '\u200C');
            entities.Add("zwj", '\u200D');
            entities.Add("lrm", '\u200E');
            entities.Add("rlm", '\u200F');
            entities.Add("ndash", '–');
            entities.Add("mdash", '—');
            entities.Add("lsquo", '‘');
            entities.Add("rsquo", '’');
            entities.Add("sbquo", '‚');
            entities.Add("ldquo", '“');
            entities.Add("rdquo", '”');
            entities.Add("bdquo", '„');
            entities.Add("dagger", '†');
            entities.Add("Dagger", '‡');
            entities.Add("permil", '‰');
            entities.Add("lsaquo", '‹');
            entities.Add("rsaquo", '›');
            entities.Add("euro", '€');
        }
    }
}