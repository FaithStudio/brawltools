﻿using BrawlLib.Modeling;
using BrawlLib.OpenGL;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.SSBBTypes;
using Gif.Components;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace System.Windows.Forms
{
    public partial class ModelEditorBase : UserControl
    {
        #region Constructor

        protected void PreConstruct()
        {
            _interpolationEditor = new Forms.InterpolationEditor(this);
        }

        protected void PostConstruct()
        {
            _timer = new CoolTimer();
            _timer.RenderFrame += _timer_RenderFrame;

            KeyframePanel.visEditor.EntryChanged += new EventHandler(VISEntryChanged);
            KeyframePanel.visEditor.IndexChanged += new EventHandler(VISIndexChanged);

            ModelPanel.PreRender += (EventPreRender = new GLRenderEventHandler(this.modelPanel1_PreRender));
            ModelPanel.PostRender += (EventPostRender = new GLRenderEventHandler(this.modelPanel1_PostRender));
            ModelPanel.MouseDown += (EventMouseDown = new System.Windows.Forms.MouseEventHandler(this.modelPanel1_MouseDown));
            ModelPanel.MouseMove += (EventMouseMove = new System.Windows.Forms.MouseEventHandler(this.modelPanel1_MouseMove));
            ModelPanel.MouseUp += (EventMouseUp = new System.Windows.Forms.MouseEventHandler(this.modelPanel1_MouseUp));

            if (PlaybackPanel != null)
                if (PlaybackPanel.Width <= PlaybackPanel.MinimumSize.Width)
                {
                    PlaybackPanel.Dock = DockStyle.Left;
                    PlaybackPanel.Width = PlaybackPanel.MinimumSize.Width;
                }
                else
                    PlaybackPanel.Dock = DockStyle.Fill;

            InitHotkeyList();

            _hotKeys = new Dictionary<Keys, Func<bool>>();
            foreach (HotKeyInfo key in _hotkeyList)
                _hotKeys.Add(key.KeyCode, key._function);
        }

        public virtual void LinkModelPanel(ModelPanel p)
        {
            p.PreRender += EventPreRender;
            p.PostRender += EventPostRender;
            p.MouseDown += EventMouseDown;
            p.MouseMove += EventMouseMove;
            p.MouseUp += EventMouseUp;
        }
        public virtual void UnlinkModelPanel(ModelPanel p)
        {
            p.PreRender -= EventPreRender;
            p.PostRender -= EventPostRender;
            p.MouseDown -= EventMouseDown;
            p.MouseMove -= EventMouseMove;
            p.MouseUp -= EventMouseUp;
        }

        public virtual void OnModelPanelChanged()
        {
            if (ModelViewerChanged != null)
                ModelViewerChanged(this, null);
        }

        #endregion

        #region Models
        public virtual void AppendTarget(IModel model)
        {
            if (!_targetModels.Contains(model))
                _targetModels.Add(model);

            ModelPanel.AddTarget(model);
            model.ResetToBindState();
        }

        protected virtual void ModelChanged(IModel newModel)
        {
            if (newModel != null && !_targetModels.Contains(newModel))
                _targetModels.Add(newModel);

            if (_targetModel != null)
                _targetModel.IsTargetModel = false;

            if ((_targetModel = newModel) != null)
            {
                ModelPanel.AddTarget(_targetModel);
                _targetModel.IsTargetModel = true;
                ResetVertexColors();
            }
            else
                EditingAll = true; //No target model so all is the only option

            if (_resetCamera)
            {
                ModelPanel.ResetCamera();
                SetFrame(0);
            }
            else
                _resetCamera = true;

            OnModelChanged();

            if (TargetModelChanged != null)
                TargetModelChanged(this, null);
        }

        protected virtual void OnModelChanged() { }
        protected virtual void OnSelectedVerticesChanged()
        {
            //Force the average vertex location to be recalculated
            _vertexLoc = null;
        }
        protected virtual void OnSelectedBoneChanged() { }

        #endregion

        #region Viewer Background

        public virtual ColorDialog ColorDialog { get { return null; } }

        public virtual void ChooseBackgroundColor()
        {
            if (ColorDialog != null && ColorDialog.ShowDialog(this) == DialogResult.OK)
                ModelPanel.BackColor = ClearColor = ColorDialog.Color;
        }

        public void ChooseOrClearBackgroundImage()
        {
            if (BGImage == null)
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "All Image Formats (*.png,*.tga,*.tif,*.tiff,*.bmp,*.jpg,*.jpeg,*.gif)|*.png;*.tga;*.tif;*.tiff;*.bmp;*.jpg;*.jpeg,*.gif|" +
                "Portable Network Graphics (*.png)|*.png|" +
                "Truevision TARGA (*.tga)|*.tga|" +
                "Tagged Image File Format (*.tif, *.tiff)|*.tif;*.tiff|" +
                "Bitmap (*.bmp)|*.bmp|" +
                "Jpeg (*.jpg,*.jpeg)|*.jpg;*.jpeg|" +
                "Gif (*.gif)|*.gif";
                d.Title = "Select an image to load";

                if (d.ShowDialog() == DialogResult.OK)
                    BGImage = Image.FromFile(d.FileName);
            }
            else
                BGImage = null;
        }

        #endregion

        #region Playback Panel
        public void pnlPlayback_Resize(object sender, EventArgs e)
        {
            if (PlaybackPanel.Width <= PlaybackPanel.MinimumSize.Width)
            {
                PlaybackPanel.Dock = DockStyle.Left;
                PlaybackPanel.Width = PlaybackPanel.MinimumSize.Width;
            }
            else
                PlaybackPanel.Dock = DockStyle.Fill;
        }

        public void numFrameIndex_ValueChanged(object sender, EventArgs e)
        {
            int val = (int)PlaybackPanel.numFrameIndex.Value;
            if (val != _animFrame)
            {
                int difference = val - _animFrame;
                if (TargetAnimation != null)
                    SetFrame(_animFrame += difference);
            }
        }
        public void numFPS_ValueChanged(object sender, EventArgs e)
        {
            _timer.TargetRenderFrequency = (double)PlaybackPanel.numFPS.Value;
        }
        public void chkLoop_CheckedChanged(object sender, EventArgs e)
        {
            _loop = PlaybackPanel.chkLoop.Checked;
            //if (TargetAnimation != null)
            //    TargetAnimation.Loop = _loop;
        }
        public void numTotalFrames_ValueChanged(object sender, EventArgs e)
        {
            if ((TargetAnimation == null) || (_updating))
                return;

            int max = (int)PlaybackPanel.numTotalFrames.Value;
            _maxFrame = max;
            PlaybackPanel.numFrameIndex.Maximum = max;

            if (Interpolated.Contains(TargetAnimation.GetType()) && TargetAnimation.Loop)
                max--;

            TargetAnimation.FrameCount = max;
        }
        public void btnPrevFrame_Click(object sender, EventArgs e) { PlaybackPanel.numFrameIndex.Value--; }
        public void btnNextFrame_Click(object sender, EventArgs e) { PlaybackPanel.numFrameIndex.Value++; }
        public void btnPlay_Click(object sender, EventArgs e)
        {
            if (_timer.IsRunning)
                StopAnim();
            else
                PlayAnim();
        }
        #endregion

        public virtual void SaveSettings() { }
        public virtual void SetDefaultSettings() { }

        private void RenderToGIF(List<Image> images, string path)
        {
            if (String.IsNullOrEmpty(path))
                return;

            string outPath = "";
            try
            {
                outPath = path;
                if (!Directory.Exists(outPath))
                    Directory.CreateDirectory(outPath);

                DirectoryInfo dir = new DirectoryInfo(outPath);
                FileInfo[] files = dir.GetFiles();
                int i = 0;
                string name = "Animation";
            Top:
                foreach (FileInfo f in files)
                    if (f.Name == name + i + ".gif")
                    {
                        i++;
                        goto Top;
                    }
                outPath += "\\" + name + i + ".gif";
            }
            catch { }

            AnimatedGifEncoder e = new AnimatedGifEncoder();
            e.Start(outPath);
            e.SetDelay(1000 / (int)PlaybackPanel.numFPS.Value);
            e.SetRepeat(0);
            e.SetQuality(1);
            using (ProgressWindow progress = new ProgressWindow(this, "GIF Encoder", "Encoding, please wait...", true))
            {
                progress.TopMost = true;
                progress.Begin(0, images.Count, 0);
                for (int i = 0, count = images.Count; i < count; i++)
                {
                    if (progress.Cancelled)
                        break;

                    e.AddFrame(images[i]);
                    progress.Update(progress.CurrentValue + 1);
                }
                progress.Finish();
                e.Finish();
            }

            MessageBox.Show("Animated GIF successfully saved to " + outPath.Replace("\\", "/"));
        }
        protected void SaveBitmap(Bitmap bmp, string path, string extension)
        {
            if (String.IsNullOrEmpty(path))
                path = Application.StartupPath + "\\ScreenCaptures";

            if (String.IsNullOrEmpty(extension))
                extension = ".png";

            if (!String.IsNullOrEmpty(path) && !String.IsNullOrEmpty(extension))
            {
                try
                {
                    string outPath = path;
                    if (!Directory.Exists(outPath))
                        Directory.CreateDirectory(outPath);

                    DirectoryInfo dir = new DirectoryInfo(outPath);
                    FileInfo[] files = dir.GetFiles();
                    int i = 0;
                    string name = "ScreenCapture";
                Top:
                    foreach (FileInfo f in files)
                        if (f.Name == name + i + extension)
                        {
                            i++;
                            goto Top;
                        }
                outPath += "\\" + name + i + extension;
                    bool okay = true;
                    if (extension.Equals(".png"))
                        bmp.Save(outPath, ImageFormat.Png);
                    else if (extension.Equals(".tga"))
                        bmp.SaveTGA(outPath);
                    else if (extension.Equals(".tiff") || extension.Equals(".tif"))
                        bmp.Save(outPath, ImageFormat.Tiff);
                    else if (extension.Equals(".bmp"))
                        bmp.Save(outPath, ImageFormat.Bmp);
                    else if (extension.Equals(".jpg") || outPath.EndsWith(".jpeg"))
                        bmp.Save(outPath, ImageFormat.Jpeg);
                    else if (extension.Equals(".gif"))
                        bmp.Save(outPath, ImageFormat.Gif);
                    else { okay = false; }
                    if (okay)
                        if (MessageBox.Show(this, "Screenshot successfully saved to \"" + outPath.Replace("\\", "/") + "\".\nOpen the folder containing the screenshot now?", "Screenshot saved", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            Process.Start("explorer.exe", path);
                }
                catch { }
            }
            bmp.Dispose();
        }
    }
}