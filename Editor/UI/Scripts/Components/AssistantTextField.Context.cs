using System;
using System.Runtime.InteropServices;
using Unity.AI.Assistant.Editor.Context;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantTextField
    {
        void HandlePaste()
        {
            if (TryHandleClipboardImage())
                return;

            HandleTextPaste();
        }

        bool TryHandleClipboardImage()
        {
            string clipboardText = EditorGUIUtility.systemCopyBuffer;

            if (TryProcessBase64Image(clipboardText))
                return true;

#if UNITY_EDITOR_WIN
            return TryGetClipboardImageWindows();
#elif UNITY_EDITOR_OSX
            return TryGetClipboardImageMac();
#else
            return false;
#endif
        }

        bool AddClipboardImageAttachment(byte[] imageBytes, string sourceFormat)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return false;

            var attachment = ScreenContextUtility.GetAttachment(imageBytes, ImageContextCategory.Image, sourceFormat);
            if (attachment != null)
            {
                attachment.DisplayName = "Clipboard Image";
                attachment.Type = "Image";
                Context.Blackboard.AddVirtualAttachment(attachment);
                Context.VirtualAttachmentAdded?.Invoke(attachment);
                return true;
            }

            return false;
        }

        bool TryProcessBase64Image(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            try
            {
                byte[] imageBytes = Convert.FromBase64String(text);
                return AddClipboardImageAttachment(imageBytes, null);  // format is unknown
            }
            catch
            {
                return false;
            }
        }

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalSize(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        const uint CF_DIB = 8;

        bool TryGetClipboardImageWindows()
        {
            if (!IsClipboardFormatAvailable(CF_DIB))
                return false;

            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                IntPtr hData = GetClipboardData(CF_DIB);
                if (hData == IntPtr.Zero)
                    return false;

                byte[] imageBytes = ConvertDibToPng(hData);
                if (imageBytes == null || imageBytes.Length == 0)
                    return false;

                return AddClipboardImageAttachment(imageBytes, "png");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Windows clipboard error: {ex.Message}");
                return false;
            }
            finally
            {
                CloseClipboard();
            }
        }

        byte[] ConvertDibToPng(IntPtr hData)
        {
            try
            {
                IntPtr pData = GlobalLock(hData);
                if (pData == IntPtr.Zero)
                    return null;

                try
                {
                    BITMAPINFOHEADER header = Marshal.PtrToStructure<BITMAPINFOHEADER>(pData);

                    if (header.biBitCount != 24 && header.biBitCount != 32)
                        return null;

                    int width = header.biWidth;
                    int height = Math.Abs(header.biHeight);
                    int bytesPerPixel = header.biBitCount / 8;
                    int stride = ((width * bytesPerPixel + 3) / 4) * 4;

                    IntPtr pixelData = IntPtr.Add(pData, (int)header.biSize);

                    byte[] bmpData = new byte[height * stride];
                    Marshal.Copy(pixelData, bmpData, 0, bmpData.Length);

                    return ConvertBmpDataToPng(bmpData, width, height, bytesPerPixel, stride, header.biHeight < 0);
                }
                finally
                {
                    GlobalUnlock(hData);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error converting DIB data: {ex.Message}");
                return null;
            }
        }

        byte[] ConvertBmpDataToPng(byte[] bmpData, int width, int height, int bytesPerPixel, int stride, bool topDown)
        {
            try
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    int srcY = topDown ? y : (height - 1 - y);
                    int srcOffset = srcY * stride;

                    for (int x = 0; x < width; x++)
                    {
                        int srcPixelOffset = srcOffset + x * bytesPerPixel;
                        int dstPixelIndex = y * width + x;

                        if (bytesPerPixel == 4)
                        {
                            pixels[dstPixelIndex] = new Color32(
                                bmpData[srcPixelOffset + 2], // R
                                bmpData[srcPixelOffset + 1], // G
                                bmpData[srcPixelOffset + 0], // B
                                bmpData[srcPixelOffset + 3]  // A
                            );
                        }
                        else if (bytesPerPixel == 3)
                        {
                            pixels[dstPixelIndex] = new Color32(
                                bmpData[srcPixelOffset + 2], // R
                                bmpData[srcPixelOffset + 1], // G
                                bmpData[srcPixelOffset + 0], // B
                                255                          // A
                            );
                        }
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();

                byte[] pngData = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);

                return pngData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating PNG: {ex.Message}");
                return null;
            }
        }
#endif

#if UNITY_EDITOR_OSX
        const string OBJC_LIB = "/usr/lib/libobjc.A.dylib";

        static readonly string[] IMAGE_TYPES = { "public.png", "public.jpeg", "public.tiff", "com.apple.pict" };

        [DllImport(OBJC_LIB)]
        static extern IntPtr objc_getClass(string className);

        [DllImport(OBJC_LIB)]
        static extern IntPtr sel_registerName(string selectorName);

        [DllImport(OBJC_LIB, EntryPoint = "objc_msgSend")]
        static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(OBJC_LIB, EntryPoint = "objc_msgSend")]
        static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(OBJC_LIB, EntryPoint = "objc_msgSend")]
        static extern ulong objc_msgSend_UInt64(IntPtr receiver, IntPtr selector);

        bool TryGetClipboardImageMac()
        {
            try
            {
                IntPtr pasteboard = GetGeneralPasteboard();
                if (pasteboard == IntPtr.Zero) return false;

                IntPtr nsStringClass = objc_getClass("NSString");
                IntPtr stringWithUTF8StringSelector = sel_registerName("stringWithUTF8String:");
                IntPtr dataForTypeSelector = sel_registerName("dataForType:");

                foreach (string imageType in IMAGE_TYPES)
                {
                    IntPtr nsStringType = CreateNSString(nsStringClass, stringWithUTF8StringSelector, imageType);
                    if (nsStringType == IntPtr.Zero) continue;

                    IntPtr imageData = objc_msgSend_IntPtr(pasteboard, dataForTypeSelector, nsStringType);
                    ReleaseNSObject(nsStringType);

                    if (imageData != IntPtr.Zero)
                    {
                        byte[] imageBytes = NSDataToByteArray(imageData);
                        if (TryCreateAttachment(imageBytes))
                            return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"macOS clipboard error: {ex.Message}");
                return false;
            }
        }

        IntPtr GetGeneralPasteboard()
        {
            IntPtr nsPasteboardClass = objc_getClass("NSPasteboard");
            IntPtr generalPasteboardSelector = sel_registerName("generalPasteboard");
            return objc_msgSend(nsPasteboardClass, generalPasteboardSelector);
        }

        IntPtr CreateNSString(IntPtr nsStringClass, IntPtr stringWithUTF8StringSelector, string str)
        {
            IntPtr cString = Marshal.StringToHGlobalAnsi(str);
            IntPtr nsString = objc_msgSend_IntPtr(nsStringClass, stringWithUTF8StringSelector, cString);
            Marshal.FreeHGlobal(cString);
            return nsString;
        }

        bool TryCreateAttachment(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return false;

            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(imageBytes)) return false;

                byte[] pngBytes = texture.EncodeToPNG();
                return AddClipboardImageAttachment(pngBytes, "png");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        byte[] NSDataToByteArray(IntPtr nsData)
        {
            IntPtr lengthSelector = sel_registerName("length");
            IntPtr bytesSelector = sel_registerName("bytes");

            ulong length = objc_msgSend_UInt64(nsData, lengthSelector);
            IntPtr bytes = objc_msgSend(nsData, bytesSelector);

            if (bytes == IntPtr.Zero || length == 0) return null;

            byte[] managedArray = new byte[length];
            Marshal.Copy(bytes, managedArray, 0, (int)length);
            return managedArray;
        }

        void ReleaseNSObject(IntPtr obj)
        {
            if (obj != IntPtr.Zero)
                objc_msgSend(obj, sel_registerName("release"));
        }
#endif

        void HandleTextPaste()
        {
            string clipboardText = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboardText))
                return;

#if UNITY_EDITOR_OSX
            // In Mac copied text (especially when copying files from Finder) comes with \r as newline delimiters,
            // so we have to translate them here to what the text field supports
            clipboardText = clipboardText.Replace('\r', '\n');
#endif
#if UNITY_EDITOR_WIN
            // On Windows copied text can come with \r\n as newline delimiters or only \r,
            // so we have to translate them here to what the text field supports
            clipboardText = clipboardText.Replace("\r\n", "\n").Replace('\r', '\n');
#endif
            
            int selectStart = Mathf.Min(m_ChatInput.cursorIndex, m_ChatInput.selectIndex);
            int selectEnd = Mathf.Max(m_ChatInput.cursorIndex, m_ChatInput.selectIndex);
            string currentText = Text;

            string newText;
            if (selectStart != selectEnd)
                newText = currentText.Substring(0, selectStart) + clipboardText + currentText.Substring(selectEnd);
            else
                newText = currentText.Insert(selectStart, clipboardText);

            if (newText.Length > m_ChatInput.maxLength)
            {
                int availableSpace = m_ChatInput.maxLength - (currentText.Length - (selectEnd - selectStart));
                if (availableSpace <= 0)
                    return;

                clipboardText = clipboardText.Substring(0, Mathf.Min(clipboardText.Length, availableSpace));
                if (selectStart != selectEnd)
                    newText = currentText.Substring(0, selectStart) + clipboardText + currentText.Substring(selectEnd);
                else
                    newText = currentText.Insert(selectStart, clipboardText);
            }

            m_ChatInput.value = newText;
            m_ChatInput.SelectRange(selectStart + clipboardText.Length, selectStart + clipboardText.Length);
        }
    }
}
