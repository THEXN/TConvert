﻿/*******************************************************************************
 *	Copyright (C) 2017  sullerandras
 *	
 *	This program is free software: you can redistribute it and/or modify
 *	it under the terms of the GNU General Public License as published by
 *	the Free Software Foundation, either version 3 of the License, or
 *	(at your option) any later version.
 *	
 *	This program is distributed in the hope that it will be useful,
 *	but WITHOUT ANY WARRANTY; without even the implied warranty of
 *	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *	GNU General Public License for more details.
 *	
 *	You should have received a copy of the GNU General Public License
 *	along with this program.  If not, see <http://www.gnu.org/licenses/>.
 ******************************************************************************/
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TConvert.Util;

namespace TConvert.Convert {
	/**<summary>A Png to Xnb Converter.</summary>*/
	public class PngConverter {
		//========== CONSTANTS ===========
		#region Constants

		private const string Texture2DType =
			"Microsoft.Xna.Framework.Content.Texture2DReader, " + 
			"Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, " + 
			"Culture=neutral, PublicKeyToken=842cf8be1de50553";

		private const int HeaderSize = 3 + 1 + 1 + 1;
		private const int CompressedFileSize = 4;
		private const int TypeReaderCountSize = 1;
		private static readonly int TypeSize = 2 + Texture2DType.Length + 4;
		private const int SharedResourceCountSize = 1;
		private const int ObjectHeaderSize = 21;
		
		private static readonly int MetadataSize =
			HeaderSize + CompressedFileSize + TypeReaderCountSize +
			TypeSize + SharedResourceCountSize + ObjectHeaderSize;

		#endregion
		//========== CONVERTING ==========
		#region Converting

		/**<summary>Converts the specified input file and writes it to the output file.</summary>*/
		public static bool Convert(string inputFile, string outputFile, bool changeExtension, bool compressed, bool reach, bool premultiply) {
			if (changeExtension) {
				outputFile = Path.ChangeExtension(outputFile, ".xnb");
			}

            // Throw more helpful exceptions than what Bitmap.ctor() throws.
            // 检查文件所在目录是否存
            if (!Directory.Exists(Path.GetDirectoryName(inputFile)))
                // 如果目录不存在，抛出 DirectoryNotFoundException 异常
                throw new DirectoryNotFoundException("无法找到路径的一部分 '" + inputFile + "'。");
            // 如果目录存在，继续检查文件是否存在
            else if (!File.Exists(inputFile))
                // 如果文件不存在，抛出 FileNotFoundException 异常
                throw new FileNotFoundException("无法找到文件 '" + inputFile + "'。");

            using (Bitmap bmp = new Bitmap(inputFile)) {
				using (FileStream stream = new FileStream(outputFile, FileMode.OpenOrCreate, FileAccess.Write)) {
					using (BinaryWriter writer = new BinaryWriter(stream)) {
						stream.SetLength(0);
						writer.Write(Encoding.UTF8.GetBytes("XNB"));    // format-identifier
						writer.Write(Encoding.UTF8.GetBytes("w"));      // target-platform
						writer.Write((byte)5);                          // xnb-format-version
						byte flagBits = 0;
						if (!reach) {
							flagBits |= 0x01;
						}
						if (compressed) {
							flagBits |= 0x80;
						}
						writer.Write(flagBits); // flag-bits; 00=reach, 01=hiprofile, 80=compressed, 00=uncompressed
						if (compressed) {
							WriteCompressedData(writer, bmp, premultiply);
						}
						else {
							writer.Write(MetadataSize + bmp.Width * bmp.Height * 4); // compressed file size
							WriteData(bmp, writer, premultiply);
						}
					}
				}
			}
			return true;
		}

		#endregion
		//=========== WRITING ============
		#region Writing

		/**<summary>Write compressed image data.</summary>*/
		private static void WriteCompressedData(BinaryWriter writer, Bitmap png, bool premultiply) {
			using (MemoryStream stream = new MemoryStream()) {
				byte[] uncompressedData;
				using (BinaryWriter writer2 = new BinaryWriter(stream)) {
					WriteData(png, writer2, premultiply);
					uncompressedData = stream.ToArray();
				}
				byte[] compressedData = XCompress.Compress(uncompressedData);
				writer.Write(6 + 4 + 4 + compressedData.Length); // compressed file size including headers
				writer.Write(uncompressedData.Length); // uncompressed data size (exluding headers! only the data)
				writer.Write(compressedData);
			}
		}
		/**<summary>Write uncompressed image data.</summary>*/
		private static void WriteData(Bitmap bmp, BinaryWriter writer, bool premultiply) {
			writer.Write7BitEncodedInt(1);                 // type-reader-count
			writer.Write7BitEncodedString(Texture2DType);  // type-reader-name
			writer.Write((int)0);                          // reader version number
			writer.Write7BitEncodedInt(0);                 // shared-resource-count

			// writing the image pixel data
			writer.Write((byte)1);
			writer.Write((int)0);
			writer.Write(bmp.Width);
			writer.Write(bmp.Height);
			writer.Write((int)1);
			writer.Write(bmp.Width * bmp.Height * 4);
			if (bmp.PixelFormat != PixelFormat.Format32bppArgb) {
				Bitmap newBmp = new Bitmap(bmp);
				bmp = newBmp.Clone(new Rectangle(0, 0, newBmp.Width, newBmp.Height), PixelFormat.Format32bppArgb);
			}
			BitmapData bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
			try {
				int length = bitmapData.Stride * bitmapData.Height;
				byte[] bytes = new byte[length];
				Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
				for (int i = 0; i < bytes.Length; i += 4) {
					// Always swap red and blue channels premultiply alpha if requested
					int a = bytes[i + 3];
					if (!premultiply || a == 255) {
						// No premultiply necessary
						byte b = bytes[i];
						bytes[i] = bytes[i + 2];
						bytes[i + 2] = b;
					}
					else if (a != 0) {
						byte b = bytes[i];
						bytes[i] = (byte) (bytes[i + 2] * a / 255);
						bytes[i + 1] = (byte) (bytes[i + 1] * a / 255);
						bytes[i + 2] = (byte) (b * a / 255);
					}
					else {
						// alpha is zero, so just zero everything
						bytes[i] = 0;
						bytes[i + 1] = 0;
						bytes[i + 2] = 0;
					}
				}
				writer.Write(bytes);
			}
			catch (Exception ex) {
				throw ex;
			}
			finally {
				bmp.UnlockBits(bitmapData);
			}
		}

		#endregion
	}
}
