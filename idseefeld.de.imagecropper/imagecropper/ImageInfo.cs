﻿using System;
using System.Drawing;
using System.Web;
using Umbraco.Core.IO;
using umbraco.cms.businesslogic.media;
using umbraco.Utils;
using Umbraco.Core.Media;


namespace idseefeld.de.imagecropper.imagecropper
{
	public class ImageInfo : PersitenceFactory
	{
		public Image image { get; set; }
		public string Name { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public float Aspect { get; set; }
		public DateTimeOffset DateStamp { get; set; }
		public string Path { get; set; }
		public string RelativePath { get; set; }

		private Config _config;
		private bool ParentIsDocument = false;
		private bool isCropBase = false;

		public ImageInfo(string relativeFilePath, Config config, bool ParentIsDocument)
		{
			this._config = config;
			this.ParentIsDocument = ParentIsDocument;
			RelativePath = relativeFilePath;
			string relPath = RelativePath.Substring(0, RelativePath.LastIndexOf('/') + 1);

			Path = IOHelper.MapPath(relativeFilePath);
			
			if (_fileSystem.FileExists(Path))
			{
				string fileName = Path.Substring(Path.LastIndexOf('\\') + 1);
				Name = fileName.Substring(0, fileName.LastIndexOf('.'));

				//byte[] buffer = null;

				//using (FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read))
				//{
				//    buffer = new byte[fs.Length];
				//    fs.Read(buffer, 0, (int)fs.Length);
				//    fs.Close();
				//}

				try
				{
					//image = Image.FromStream(new System.IO.MemoryStream(buffer));
					image = Image.FromStream(_fileSystem.OpenFile(Path));

					Width = image.Width;
					Height = image.Height;
					Aspect = (float)Width / Height;
					DateStamp = _fileSystem.GetLastModified(Path);

					if (config.ResizeMax > 0 && !isCropBase)
					{
						string newPath = CreateCropBaseImage(config.ResizeMax, Width, Height, Aspect, DateStamp, config.ResizeEngine);
						if (!String.IsNullOrEmpty(newPath))
						{
							Path = newPath;
							fileName = Path.Substring(Path.LastIndexOf('\\') + 1);
							RelativePath = relPath + fileName;
							Name = fileName.Substring(0, fileName.LastIndexOf('.'));
							//using (FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read))
							//{
							//    buffer = new byte[fs.Length];
							//    fs.Read(buffer, 0, (int)fs.Length);
							//    fs.Close();
							//}
							try
							{
								//image = Image.FromStream(new MemoryStream(buffer));
								image = Image.FromStream(_fileSystem.OpenFile(Path));

								Width = image.Width;
								Height = image.Height;
								Aspect = (float)Width / Height;
								DateStamp = _fileSystem.GetLastModified(Path);
								isCropBase = true;
							}
							catch { }
						}
					}
				}
				catch (Exception)
				{
					Width = 0;
					Height = 0;
					Aspect = 0;
				}
			}
			else
			{
				Width = 0;
				Height = 0;
				Aspect = 0;
			}
		}
		private string CreateCropBaseImage(int maxWidth, int width, int height, float aspect, DateTimeOffset dateStamp, IImageResizeEngine ResizeEngine)
		{
			if (maxWidth >= width || Name.EndsWith("_cb"))
				return "";

			string cropBaseName = String.Format("{0}_{1}", Name, "cb");
			string path = Path.Substring(0, Path.LastIndexOf('\\'));
			string ext = ImageTransform.GetAdjustedFileExtension(Path);// Path.Substring(Path.LastIndexOf('.') + 1);
			string newPath = String.Format("{0}\\{1}.{2}", path, cropBaseName, ext);
			if (this._fileSystem.FileExists(newPath))
			{
				DateTimeOffset cropDateStamp = _fileSystem.GetLastModified(newPath);
				if (cropDateStamp.CompareTo(dateStamp) > 0)
					return newPath;
			}

			bool forceResize = true;//must
			ResizeEngine.saveNewImageSize(Path, ext, newPath, maxWidth, forceResize, width, _config.IgnoreICC, false);

			return newPath;
		}
		public bool Exists
		{
			get { return Width > 0 && Height > 0; }
		}

		public string Directory
		{
			get { return Path.Substring(0, Path.LastIndexOf('\\')); }
		}

		public void GenerateThumbnails(SaveData saveData, Config config)
		{
			if (config.GenerateImages)
			{
				for (int i = 0; i < config.presets.Count; i++)
				{
					Crop crop = (Crop)saveData.data[i];
					Preset preset = (Preset)config.presets[i];

					int cX = crop.X;
					int cY = crop.Y;
					int cWidth = crop.X2 - crop.X;
					int cHeight = crop.Y2 - crop.Y;
					int tWidth = preset.TargetWidth;
					int tHeight = preset.TargetHeight;
					if (tWidth == 0)
					{
						tWidth = tHeight * cWidth / cHeight;
					}
					else if (tHeight == 0)
					{
						tHeight = tWidth * cHeight / cWidth;
					}

					// Crop rectangle bigger than actual image
					if (cWidth > Width)
					{
						cWidth = Width;
						cX = 0;
					}
					if (cHeight > Height)
					{
						cHeight = Height;
						cY = 0;
					}

					string newPath = ImageTransform.Execute(
						Path,
						String.Format("{0}_{1}", Name, preset.Name),
						cX,
						cY,
						cWidth,
						cHeight,
						tWidth,
						tHeight,
						config,
						_fileSystem
					);
					if (ParentIsDocument)
					{
						//support preview / publish
						string cropHash = config.cropHashDict[preset.Name];
						string newFile = CopyToHashFile(
												newPath,
												String.Format("{0}",
													cropHash)
											);
						config.newCropFiles.Add(newFile);
					}
				}
			}
		}
	}

	public class ImageTransform
	{
		public static string GetAdjustedFileExtension(string filename)
		{
			string extension = filename.Substring(filename.LastIndexOf('.') + 1);
			if (extension.StartsWith("tif", StringComparison.InvariantCultureIgnoreCase))
				extension = "jpg";//because the image engine supports tiff images, but web browsers do not!

			return extension;
		}
		public static string FixBrowserUnsupportedFormats(string relativeImagePath, IImageResizeEngine ResizeEngine)
		{
			string adjustedPath = relativeImagePath;
			string extension = relativeImagePath.Substring(relativeImagePath.LastIndexOf('.') + 1);
			if (extension.StartsWith("tif", StringComparison.InvariantCultureIgnoreCase))
			{
				extension = "jpg";
				adjustedPath = String.Format("{0}.{1}",
					adjustedPath.Substring(0, adjustedPath.LastIndexOf('.')),
					extension);
				string sourceFile = IOHelper.MapPath(relativeImagePath);
				string newFile = IOHelper.MapPath(adjustedPath);
				
				ResizeEngine.saveNewImageSize(sourceFile, extension, newFile, true);
			}
			return adjustedPath;
		}
		public static string Execute(string sourceFile, string name, int cropX, int cropY, int cropWidth, int cropHeight, int sizeWidth, int sizeHeight, Config config, MediaFileSystem _fileSystem)
		{
			string result = "";
			if (!_fileSystem.FileExists(sourceFile)) return result;

			string path = sourceFile.Substring(0, sourceFile.LastIndexOf('\\'));
			string ext = ImageTransform.GetAdjustedFileExtension(sourceFile);
			string newPath = String.Format("{0}\\{1}.{2}", path, name, ext);
			bool forceResize = true;//must
			config.ResizeEngine.saveCroppedNewImageSize(sourceFile, ext, newPath, sizeWidth, forceResize, 0, sizeHeight, cropX, cropY, cropWidth, cropHeight, config.Quality, config.IgnoreICC, false);
			result = newPath;
			return result;
		}

		
	}
}
