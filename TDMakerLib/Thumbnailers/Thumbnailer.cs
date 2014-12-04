﻿using HelpersLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace TDMakerLib
{
    public class Thumbnailer
    {
        protected string ThumbnailerPath;

        protected ThumbnailerOptions Options = new ThumbnailerOptions();

        protected MediaFile MediaFile { get; set; }
        protected string ScreenshotDir { get; set; }

        protected List<ScreenshotInfo> TempScreenshots = new List<ScreenshotInfo>();
        public List<ScreenshotInfo> Screenshots = new List<ScreenshotInfo>();

        public string MediaSummary { get; protected set; }

        protected Thumbnailer()
        {
        }

        public Thumbnailer(MediaFile mf, string ssDir, ThumbnailerOptions options)
        {
            MediaFile = mf;
            ScreenshotDir = ssDir;
            Options = options;
        }

        public virtual void TakeScreenshot()
        {
            string MPlayerTempFp = Path.Combine(ScreenshotDir, "00000001.png"); // MPlayer creates this file by default

            switch (App.Settings.ThumbnailerType)
            {
                case ThumbnailerType.MPlayer:
                    ThumbnailerPath = App.Settings.MPlayerPath;
                    if (File.Exists(MPlayerTempFp)) File.Delete(MPlayerTempFp);
                    break;
                case ThumbnailerType.FFmpeg:
                    ThumbnailerPath = App.Settings.FFmpegPath;
                    break;
            }

            int time_slice = GetTimeSlice(Options.ScreenshotCount);

            for (int i = 0; i < Options.ScreenshotCount; i++)
            {
                int timeSliceElapsed = time_slice * (i + 1);
                string tempScreenshotPath = Path.Combine(ScreenshotDir, string.Format("{0}-{1}.png", Path.GetFileNameWithoutExtension(MediaFile.FilePath), timeSliceElapsed));

                ProcessStartInfo psi = new ProcessStartInfo(ThumbnailerPath);
                psi.WindowStyle = ProcessWindowStyle.Minimized;

                switch (App.Settings.ThumbnailerType)
                {
                    case ThumbnailerType.MPlayer:
                        psi.Arguments = string.Format("-nosound -ss {0} -zoom -vf screenshot -frames 1 -vo png:z=9:outdir=\\\"{1}\\\" \"{2}\"",
                               timeSliceElapsed, ScreenshotDir, MediaFile.FilePath);
                        break;
                    case ThumbnailerType.FFmpeg:
                        psi.Arguments = string.Format("-i \"{0}\" -ss {1} -f image2 -vframes 1 \"{2}\"", MediaFile.FilePath, timeSliceElapsed, tempScreenshotPath);
                        break;
                }

                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                p.WaitForExit(1000 * 30);

                switch (App.Settings.ThumbnailerType)
                {
                    case ThumbnailerType.MPlayer:
                        if (File.Exists(MPlayerTempFp))
                        {
                            if (File.Exists(tempScreenshotPath)) File.Delete(tempScreenshotPath);
                            File.Move(MPlayerTempFp, tempScreenshotPath);
                        }
                        break;
                }

                if (File.Exists(tempScreenshotPath))
                {
                    ScreenshotInfo screenshotInfo = new ScreenshotInfo(tempScreenshotPath)
                    {
                        Args = psi.Arguments,
                        Timestamp = TimeSpan.FromSeconds(timeSliceElapsed)
                    };

                    TempScreenshots.Add(screenshotInfo);
                }
            } // for loop

            if (TempScreenshots.Count > 0)
            {
                if (Options.CombineScreenshots)
                {
                    using (Image img = CombineScreenshots(TempScreenshots))
                    {
                        string temp_fp = Path.Combine(ScreenshotDir, Path.GetFileNameWithoutExtension(MediaFile.FilePath) + "_s.png");
                        img.Save(temp_fp, ImageFormat.Png);
                        this.Screenshots.Add(new ScreenshotInfo(temp_fp) { Args = TempScreenshots[0].Args });
                    }
                }
                else
                {
                    this.Screenshots.AddRange(TempScreenshots);
                }
            }
        }

        public int GetTimeSlice(int NumScreenshots)
        {
            return (int)(MediaFile.SegmentDuration / ((NumScreenshots + 1) * 1000));
        }

        private Image CombineScreenshots(List<ScreenshotInfo> screenshots)
        {
            List<Image> images = new List<Image>();
            Image finalImage = null;

            try
            {
                string infoString = "";
                int infoStringHeight = 0;

                if (Options.AddMovieInfo)
                {
                    infoString = MediaFile.GetMTNString();
                    infoStringHeight = 80;
                }

                foreach (ScreenshotInfo screenshot in screenshots)
                {
                    Image img = Image.FromFile(screenshot.LocalPath);

                    if (Options.MaxThumbnailWidth > 0 && img.Width > Options.MaxThumbnailWidth)
                    {
                        int maxThumbnailHeight = (int)((float)Options.MaxThumbnailWidth / img.Width * img.Height);
                        img = ImageHelpers.ResizeImage(img, Options.MaxThumbnailWidth, maxThumbnailHeight);
                    }

                    images.Add(img);
                }

                int columnCount = Options.ColumnCount;

                int thumbWidth = images[0].Width;

                int width = Options.Padding * 2 +
                    thumbWidth * columnCount +
                    (columnCount - 1) * Options.Spacing;

                int rowCount = (int)Math.Ceiling(images.Count / (float)columnCount);

                int thumbHeight = images[0].Height;

                int height = Options.Padding * 3 +
                    infoStringHeight +
                    thumbHeight * rowCount +
                    (rowCount - 1) * Options.Spacing;

                finalImage = new Bitmap(width, height);

                using (Graphics g = Graphics.FromImage(finalImage))
                {
                    g.Clear(Color.WhiteSmoke);

                    if (!string.IsNullOrEmpty(infoString))
                    {
                        using (Font font = new Font("Arial", 14))
                        {
                            g.DrawString(infoString, font, Brushes.Black, Options.Padding, Options.Padding);
                        }
                    }

                    int i = 0;
                    int offsetY = Options.Padding * 2 + infoStringHeight;

                    for (int y = 0; y < rowCount; y++)
                    {
                        int offsetX = Options.Padding;

                        for (int x = 0; x < columnCount; x++)
                        {
                            if (Options.DrawShadow)
                            {
                                int shadowOffset = 3;

                                using (Brush shadowBrush = new SolidBrush(Color.FromArgb(50, Color.Black)))
                                {
                                    g.FillRectangle(shadowBrush, offsetX + shadowOffset, offsetY + shadowOffset, thumbWidth, thumbHeight);
                                }
                            }

                            g.DrawImage(images[i], offsetX, offsetY, thumbWidth, thumbHeight);

                            if (Options.AddTimestamp)
                            {
                                int timestampOffset = 10;

                                using (Font font = new Font("Arial", 12))
                                {
                                    ImageHelpers.DrawTextWithShadow(g, screenshots[i].Timestamp.ToString(),
                                        new Point(offsetX + timestampOffset, offsetY + timestampOffset), font, Color.White, Color.Black);
                                }
                            }

                            i++;

                            if (i >= images.Count)
                            {
                                return finalImage;
                            }

                            offsetX += thumbWidth + Options.Spacing;
                        }

                        offsetY += thumbHeight + Options.Spacing;
                    }
                }

                return finalImage;
            }
            catch
            {
                if (finalImage != null)
                {
                    finalImage.Dispose();
                }

                throw;
            }
            finally
            {
                foreach (Image image in images)
                {
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }
    }

    public class ThumbnailerOptions
    {
        [Category("Options"), DefaultValue(3), Description("Number of screenshots to take")]
        public int ScreenshotCount { get; set; }

        [Category("Combine screenshots"), DefaultValue(false), Description("Combine all screenshots to one large screenshot")]
        public bool CombineScreenshots { get; set; }

        [Category("Combine screenshots"), DefaultValue(0), Description("Maximum thumbnail width size, 0 means don't resize")]
        public int MaxThumbnailWidth { get; set; }

        [Category("Combine screenshots"), DefaultValue(20), Description("Space between border and content as pixel")]
        public int Padding { get; set; }

        [Category("Combine screenshots"), DefaultValue(10), Description("Space between screenshots as pixel")]
        public int Spacing { get; set; }

        [Category("Combine screenshots"), DefaultValue(1), Description("Number of screenshots per row")]
        public int ColumnCount { get; set; }

        [Category("Combine screenshots"), DefaultValue(true), Description("Add movie information to the combined screenshot")]
        public bool AddMovieInfo { get; set; }

        [Category("Combine screenshots"), DefaultValue(true), Description("Add timestamp of screenshot at corner of image")]
        public bool AddTimestamp { get; set; }

        [Category("Combine screenshots"), DefaultValue(true), Description("Draw rectangle shadow behind thumbnails")]
        public bool DrawShadow { get; set; }

        public ThumbnailerOptions()
        {
            this.ApplyDefaultPropertyValues();
        }
    }
}