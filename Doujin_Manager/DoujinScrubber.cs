﻿using Doujin_Manager.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Doujin_Manager
{
    class DoujinScrubber
    {
        public void PopulateDoujins(CentralViewModel dataContext)
        {
            if (!Directory.Exists(Properties.Settings.Default.DoujinDirectory))
                return;

            string[] allSubDirectories;

            try
            {
                allSubDirectories = Directory.GetDirectories(Properties.Settings.Default.DoujinDirectory, "*", SearchOption.AllDirectories);
            }
            catch(UnauthorizedAccessException e)
            {
                MessageBox.Show(e.Message,
                    "Unauthorized Access Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            List<string> potentialDoujinDirectories = new List<string>();

            TagScrubber tagScrubber = new TagScrubber();

            // Search all subdirectories which contain an image
            foreach (string directory in allSubDirectories)
            {
                string[] files = Directory.GetFiles(directory);

                if (files.Any(s => s.ToLower().EndsWith(".jpg")) ||
                    files.Any(s => s.ToLower().EndsWith(".png")) ||
                    files.Any(s => s.ToLower().EndsWith(".jpeg")))
                {
                    potentialDoujinDirectories.Add(directory);
                }
            }

            // Search for the first image in the directory 
            // and set it as cover image + add it to the panel
            foreach (string directory in potentialDoujinDirectories)
            {
                string[] files = Directory.GetFiles(directory);
                string coverImagePath = "";
                string dirName = "";

                foreach (string file in files)
                {
                    if (file.ToLower().EndsWith(".jpg") ||
                        file.ToLower().EndsWith(".png") ||
                        file.ToLower().EndsWith(".jpeg"))
                    {
                        coverImagePath = CompressImage(file);
                        dirName = Path.GetFileName(Path.GetDirectoryName(file));
                        break;
                    }
                }

                tagScrubber.GatherDoujinDetails(dirName, TagScrubber.SearchMode.Title);

                try
                {   // evokes the main (UI) thread to add the Doujin
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        BitmapImage coverImage = new BitmapImage(new Uri(coverImagePath))
                        {
                            CacheOption = BitmapCacheOption.None,
                            CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                            DecodePixelWidth = 140
                        };
                        Doujin doujin = new Doujin
                        {
                            CoverImage = coverImage,
                            Title = dirName,
                            Author = tagScrubber.Author,
                            Directory = directory,
                            Tags = tagScrubber.Tags,
                            ID = tagScrubber.ID
                        };

                        dataContext.DoujinsViewModel.Doujins.Add(doujin);
                        dataContext.DoujinInfoViewModel.Count = dataContext.DoujinsViewModel.Doujins.Count.ToString();

                    }));
                }
                catch
                {
                    Debug.WriteLine("Failed to add doujin at: " + coverImagePath);
                }

                tagScrubber.Clear();

                // Add delay to prevent sending too many requests to nHentai
                // currently still results in a ban (403 Forbidden)
                //System.Threading.Thread.Sleep(500);
            }
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                CacheToJson(dataContext);
            }));
        }

        private void CacheToJson(CentralViewModel dataContext)
        {
            List<Doujin> doujins = dataContext.DoujinsViewModel.Doujins.ToList<Doujin>();
            string[] aDoujins = new string[doujins.Count];

            for (int i = 0; i < doujins.Count; i++)
            {
                aDoujins[i] = JsonConvert.SerializeObject(doujins[i], Formatting.Indented);
            }

            File.WriteAllLines(DirectoryInfo.cacheFilePath, aDoujins);
        }

        private string CompressImage(string imagePath)
        {
            string fileName = Path.GetFileName(imagePath);
            string lastFolderName = Path.GetFileName(Path.GetDirectoryName(imagePath));
            string fileCompressedPath = DirectoryInfo.thumbnailDir + "\\" + lastFolderName + " - "+ fileName;

            if (File.Exists(fileCompressedPath))
                return fileCompressedPath;

            try
            {
                Bitmap image = new Bitmap(imagePath);
                SaveCompressedImage(fileCompressedPath, image, 40);
                Debug.WriteLine("Image compressed: " + fileCompressedPath);
            }
            catch
            {
                Debug.WriteLine("Image failed to be compressed: " + imagePath);
                return imagePath;
            }

            return fileCompressedPath;
        }

        // More quality = higher quality image
        private void SaveCompressedImage(string path, Bitmap image, int quality)
        {
            if (quality < 0 || quality > 100)
                throw new ArgumentOutOfRangeException("Quality must be between 0 and 100.");

            // Encoder parameter for image quality 
            EncoderParameter qualityParam = new EncoderParameter(Encoder.Quality, quality);

            // Image codec 
            ImageCodecInfo codec = GetEncoderInfo(GetMimeType(path));

            EncoderParameters encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = qualityParam;

            image.Save(path, codec, encoderParams);
        }

        private string GetMimeType(string path)
        {
            switch (Path.GetExtension(path).ToLower())
            {
                case ".jpeg":
                case ".jpg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                default:
                    return "";
            }
        }

        private ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            // Get image codecs for all image formats 
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();

            // Find the correct image codec 
            for (int i = 0; i < codecs.Length; i++)
                if (codecs[i].MimeType == mimeType)
                    return codecs[i];
            return null;
        }
    }
}
