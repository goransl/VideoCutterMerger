// File: Program.cs
// Build: dotnet new winforms -n FfmpegCutterMerger -f net8.0-windows
// Replace Program.cs with this file. Put ffmpeg.exe and ffprobe.exe beside the .exe or set paths in Settings.
// Run: dotnet build && run

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FfmpegCutterMerger
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        // Settings — adjust these if you want hardcoded paths:
        private string FFMPEG_PATH = @"C:\Users\goran\Desktop\backup\1SORTED\eror\c\ffmpeg-7.1-full_build\bin\ffmpeg.exe";
        private string FFPROBE_PATH = @"C:\Users\goran\Desktop\backup\1SORTED\eror\c\ffmpeg-7.1-full_build\bin\ffprobe.exe";

        private readonly TextBox txtInput = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        private readonly Button btnBrowseInput = new() { Text = "Input…" };
        private readonly TextBox txtOutput = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        private readonly Button btnBrowseOutput = new() { Text = "Output…" };
        private readonly TextBox txtStart = new() { Text = "00:00:00" };
        private readonly TextBox txtEnd = new() { Text = "00:00:10" };
        private readonly CheckBox chkReencode = new() { Text = "Re-encode (frame-accurate)" };
        private readonly Button btnCut = new() { Text = "Cut" };
        private readonly ProgressBar progress = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, Minimum = 0, Maximum = 100 };

        // Merge UI
        private readonly ListBox lstMerge = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom };
        private readonly Button btnAdd = new() { Text = "Add…" };
        private readonly Button btnRemove = new() { Text = "Remove" };
        private readonly Button btnUp = new() { Text = "Up" };
        private readonly Button btnDown = new() { Text = "Down" };
        private readonly TextBox txtMergeOut = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        private readonly Button btnMergeOut = new() { Text = "Output…" };
        private readonly Button btnMergeCopy = new() { Text = "Merge (no re-encode)" };
        private readonly Button btnMergeRe = new() { Text = "Merge (re-encode)" };
		
		// --- Cut output auto-tracking ---
		private bool _cutOutputIsAuto = true;        // we auto-update output until user edits it
		private bool _suppressOutputChanged = false; // prevents feedback loop when we set it programmatically


        private FfmpegRunner? ff;

        public MainForm()
        {

            Text = "FFmpeg Cutter & Merger";
            Width = 900;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabCut = new TabPage("Cut");
            var tabMerge = new TabPage("Merge");
            tabs.TabPages.Add(tabCut);
            tabs.TabPages.Add(tabMerge);
            Controls.Add(tabs);

            // ----- CUT LAYOUT -----
            var pnlCut = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 7,
                Padding = new Padding(10)
            };
            pnlCut.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlCut.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlCut.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlCut.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int r = 0;
            pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(new Label { Text = "Input:", AutoSize = true }, 0, r);
            pnlCut.Controls.Add(txtInput, 1, r);
            pnlCut.SetColumnSpan(txtInput, 2);
            pnlCut.Controls.Add(btnBrowseInput, 3, r);
			
			// Drag & drop onto the Cut input textbox
			txtInput.AllowDrop = true;
			txtInput.DragEnter += TxtInput_DragEnter;
			txtInput.DragDrop  += TxtInput_DragDrop;


            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(new Label { Text = "Output:", AutoSize = true }, 0, r);
            pnlCut.Controls.Add(txtOutput, 1, r);
            pnlCut.SetColumnSpan(txtOutput, 2);
            pnlCut.Controls.Add(btnBrowseOutput, 3, r);
			
			txtInput.TextChanged  += TxtInput_TextChanged;
			txtOutput.TextChanged += TxtOutput_TextChanged;

            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(new Label { Text = "Start (hh:mm:ss):", AutoSize = true }, 0, r);
            pnlCut.Controls.Add(txtStart, 1, r);
            pnlCut.Controls.Add(new Label { Text = "End (hh:mm:ss):", AutoSize = true }, 2, r);
            pnlCut.Controls.Add(txtEnd, 3, r);

            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(chkReencode, 1, r);
            pnlCut.SetColumnSpan(chkReencode, 3);

            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(btnCut, 3, r);

            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            // spacer row

            r++; pnlCut.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlCut.Controls.Add(progress, 0, r);
            pnlCut.SetColumnSpan(progress, 4);

            tabCut.Controls.Add(pnlCut);

			btnMergeCopy.Dock = DockStyle.Fill;
			btnMergeRe.Dock   = DockStyle.Fill;
			// ----- MERGE LAYOUT (clean version) -----
			var pnlMerge = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 3,
				Padding = new Padding(12)
			};
			pnlMerge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // left: list
			pnlMerge.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // right: small buttons
			pnlMerge.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // main area
			pnlMerge.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // output row
			pnlMerge.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // bottom buttons row

			// Main area: list on the left
			lstMerge.Dock = DockStyle.Fill;
			pnlMerge.Controls.Add(lstMerge, 0, 0);
			
			// Drag & drop onto the merge list
			lstMerge.AllowDrop = true;
			lstMerge.HorizontalScrollbar = true;
			lstMerge.DragEnter += LstMerge_DragEnter;
			lstMerge.DragDrop  += LstMerge_DragDrop;


			// Main area: vertical small buttons on the right
			var rightButtons = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(0),
				Margin = new Padding(12, 0, 0, 0)
			};
			rightButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			rightButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			rightButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			rightButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			rightButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			btnAdd.AutoSize = true;
			btnRemove.AutoSize = true;
			btnUp.AutoSize = true;
			btnDown.AutoSize = true;

			rightButtons.Controls.Add(btnAdd,   0, 0);
			rightButtons.Controls.Add(btnRemove,0, 1);
			rightButtons.Controls.Add(btnUp,    0, 2);
			rightButtons.Controls.Add(btnDown,  0, 3);

			pnlMerge.Controls.Add(rightButtons, 1, 0);

			// Output row
			var outRow = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 3,
				Padding = new Padding(0, 8, 0, 0)
			};
			outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // "Output:" label
			outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));    // textbox
			outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // button

			var lblOut = new Label { Text = "Output:", AutoSize = true, Margin = new Padding(0, 6, 8, 6) };
			txtMergeOut.Dock = DockStyle.Fill;
			btnMergeOut.AutoSize = true;

			outRow.Controls.Add(lblOut,     0, 0);
			outRow.Controls.Add(txtMergeOut,1, 0);
			outRow.Controls.Add(btnMergeOut,2, 0);

			pnlMerge.Controls.Add(outRow, 0, 1);
			pnlMerge.SetColumnSpan(outRow, 2);

			// Bottom bar: merge buttons right-aligned, padded, auto-sized
			btnMergeCopy.AutoSize = true;
			btnMergeCopy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			btnMergeCopy.Padding = new Padding(12, 6, 12, 6);

			btnMergeRe.AutoSize = true;
			btnMergeRe.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			btnMergeRe.Padding = new Padding(12, 6, 12, 6);

			var bottomButtons = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.RightToLeft,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Padding = new Padding(0, 8, 0, 0)
			};
			bottomButtons.Controls.Add(btnMergeRe);
			bottomButtons.Controls.Add(btnMergeCopy);
			// (FlowDirection RightToLeft makes them align to the right; Copy will appear to the left of Re)

			pnlMerge.Controls.Add(bottomButtons, 0, 2);
			pnlMerge.SetColumnSpan(bottomButtons, 2);

			tabMerge.Controls.Add(pnlMerge);
			// ----- END MERGE LAYOUT -----

            // Wire up
            btnBrowseInput.Click += (_, __) => BrowseFile(txtInput, "Video files|*.mp4;*.mkv;*.mov;*.ts;*.avi|All files|*.*");
            btnBrowseOutput.Click += (_, __) => SaveFile(txtOutput, "MKV|*.mkv|MP4|*.mp4|All files|*.*");
            btnMergeOut.Click += (_, __) => SaveFile(txtMergeOut, "MKV|*.mkv|MP4|*.mp4|All files|*.*");

			btnAdd.Click += (_, __) =>
			{
				using var ofd = new OpenFileDialog
				{
					Filter = "Video files|*.mp4;*.mkv;*.mov;*.ts;*.avi|All files|*.*",
					Multiselect = true
				};
				if (ofd.ShowDialog(this) == DialogResult.OK)
				{
					bool wasEmpty = lstMerge.Items.Count == 0;
					if (wasEmpty && ofd.FileNames.Length > 0)
						SetDefaultMergeOutputIfFirst(ofd.FileNames[0]);

					foreach (var p in ofd.FileNames)
						if (!lstMerge.Items.Contains(p)) lstMerge.Items.Add(p);
				}
			};

            btnRemove.Click += (_, __) =>
            {
                while (lstMerge.SelectedIndices.Count > 0)
                    lstMerge.Items.RemoveAt(lstMerge.SelectedIndices[0]);
            };
            btnUp.Click += (_, __) => MoveSelected(-1);
            btnDown.Click += (_, __) => MoveSelected(1);

            btnCut.Click += async (_, __) => await DoCutAsync();
            btnMergeCopy.Click += async (_, __) => await DoMergeCopyAsync();
            btnMergeRe.Click += async (_, __) => await DoMergeReencodeAsync();

            Shown += (_, __) =>
            {
                // Autodetect ffmpeg/ffprobe next to app if available
                var exeDir = AppContext.BaseDirectory;
                var altFfmpeg = Path.Combine(exeDir, "ffmpeg.exe");
                var altFfprobe = Path.Combine(exeDir, "ffprobe.exe");
                if (File.Exists(altFfmpeg)) FFMPEG_PATH = altFfmpeg;
                if (File.Exists(altFfprobe)) FFPROBE_PATH = altFfprobe;

                try { ff = new FfmpegRunner(FFMPEG_PATH, FFPROBE_PATH); }
                catch (Exception e)
                {
                    MessageBox.Show(this, e.Message, "FFmpeg not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
        }

        private void BrowseFile(TextBox target, string filter)
        {
            using var ofd = new OpenFileDialog { Filter = filter };
            if (ofd.ShowDialog(this) == DialogResult.OK) target.Text = ofd.FileName;
        }

        private void SaveFile(TextBox target, string filter)
        {
            using var sfd = new SaveFileDialog { Filter = filter, OverwritePrompt = true };
            if (sfd.ShowDialog(this) == DialogResult.OK) target.Text = sfd.FileName;
        }

        private void MoveSelected(int dir)
        {
            if (lstMerge.SelectedItem == null) return;
            int idx = lstMerge.SelectedIndex;
            int newIdx = idx + dir;
            if (newIdx < 0 || newIdx >= lstMerge.Items.Count) return;
            var item = lstMerge.Items[idx];
            lstMerge.Items.RemoveAt(idx);
            lstMerge.Items.Insert(newIdx, item);
            lstMerge.SelectedIndex = newIdx;
        }

        private async Task DoCutAsync()
        {
            if (ff == null) { MessageBox.Show(this, "FFmpeg not configured."); return; }
            string input = txtInput.Text;
            string output = txtOutput.Text;
            string start = txtStart.Text.Trim();
            string end = txtEnd.Text.Trim();
            bool reencode = chkReencode.Checked;

            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { MessageBox.Show(this, "Pick a valid input file."); return; }
            if (string.IsNullOrWhiteSpace(output)) { MessageBox.Show(this, "Pick an output path."); return; }

            ToggleUI(false);
            var prog = new Progress<double>(p => progress.Value = Math.Min(100, Math.Max(0, (int)Math.Round(p * 100))));
            try
            {
                await CutAsync(ff, input, output, start, end, reencode, prog, CancellationToken.None);
                progress.Value = 100;
                MessageBox.Show(this, "Cut done.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cut error");
            }
            finally { ToggleUI(true); }
        }

        private async Task DoMergeCopyAsync()
        {
            if (ff == null) { MessageBox.Show(this, "FFmpeg not configured."); return; }
            if (lstMerge.Items.Count < 2) { MessageBox.Show(this, "Add at least two files."); return; }
            if (string.IsNullOrWhiteSpace(txtMergeOut.Text)) { MessageBox.Show(this, "Pick merge output path."); return; }

            ToggleUI(false);
            var prog = new Progress<double>(p => progress.Value = Math.Min(100, Math.Max(0, (int)Math.Round(p * 100))));
            try
            {
                var inputs = new string[lstMerge.Items.Count];
                lstMerge.Items.CopyTo(inputs, 0);
                await MergeCopyAsync(ff, inputs, txtMergeOut.Text, prog, CancellationToken.None);
                progress.Value = 100;
                MessageBox.Show(this, "Merge (copy) done.");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Merge error"); }
            finally { ToggleUI(true); }
        }

        private async Task DoMergeReencodeAsync()
        {
            if (ff == null) { MessageBox.Show(this, "FFmpeg not configured."); return; }
            if (lstMerge.Items.Count < 2) { MessageBox.Show(this, "Add at least two files."); return; }
            if (string.IsNullOrWhiteSpace(txtMergeOut.Text)) { MessageBox.Show(this, "Pick merge output path."); return; }

            ToggleUI(false);
            var prog = new Progress<double>(p => progress.Value = Math.Min(100, Math.Max(0, (int)Math.Round(p * 100))));
            try
            {
                var inputs = new string[lstMerge.Items.Count];
                lstMerge.Items.CopyTo(inputs, 0);
                await MergeReencodeAsync(ff, inputs, txtMergeOut.Text, prog, CancellationToken.None);
                progress.Value = 100;
                MessageBox.Show(this, "Merge (re-encode) done.");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Merge error"); }
            finally { ToggleUI(true); }
        }

        private void ToggleUI(bool enable)
        {
            foreach (Control c in Controls) c.Enabled = enable;
            // Keep progress active
            progress.Enabled = true;
        }

        // ---------- FFmpeg Ops ----------
        private static async Task CutAsync(FfmpegRunner ff, string input, string output,
                                           string startHHMMSS, string endHHMMSS,
                                           bool reencode, IProgress<double>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var total = await ff.GetDurationSecondsAsync(input, ct);

            string argsFast =
                $"-y -ss {startHHMMSS} -to {endHHMMSS} -i {FfmpegRunner.Q(input)} -c copy {FfmpegRunner.Q(output)}";

            string argsRe =
                $"-y -ss {startHHMMSS} -to {endHHMMSS} -i {FfmpegRunner.Q(input)} " +
                "-c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k " +
                FfmpegRunner.Q(output);

            int exit = await ff.RunFfmpegAsync(reencode ? argsRe : argsFast, total, progress, ct);
            if (exit != 0) throw new Exception($"ffmpeg cut failed with exit code {exit}");
        }

        private static async Task MergeCopyAsync(FfmpegRunner ff, string[] inputs, string output,
                                                 IProgress<double>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            string listPath = Path.Combine(Path.GetDirectoryName(output)!, "input.txt");
            await File.WriteAllLinesAsync(
				listPath,
				Array.ConvertAll(inputs, p => $"file '{p}'"),
				ct
			);


            double total = 0;
            foreach (var i in inputs) total += await ff.GetDurationSecondsAsync(i, ct) ?? 0;

            string args = $"-y -f concat -safe 0 -i {FfmpegRunner.Q(listPath)} -c copy {FfmpegRunner.Q(output)}";
            int exit = await ff.RunFfmpegAsync(args, total, progress, ct);
            if (exit != 0) throw new Exception($"ffmpeg merge (copy) failed with exit code {exit}");
        }

        private static async Task MergeReencodeAsync(FfmpegRunner ff, string[] inputs, string output,
                                                     IProgress<double>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            var sb = new StringBuilder("-y ");
            foreach (var i in inputs) sb.Append($"-i {FfmpegRunner.Q(i)} ");

            int n = inputs.Length;
            var streams = new StringBuilder();
            for (int idx = 0; idx < n; idx++) streams.Append($"[{idx}:v:0][{idx}:a:0]");
            var filter = $"{streams}concat=n={n}:v=1:a=1[outv][outa]";

            sb.Append($"-filter_complex {FfmpegRunner.Q(filter)} -map [outv] -map [outa] ");
            sb.Append("-c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k ");
            sb.Append(FfmpegRunner.Q(output));

            double total = 0;
            foreach (var i in inputs) total += await ff.GetDurationSecondsAsync(i, ct) ?? 0;

            int exit = await ff.RunFfmpegAsync(sb.ToString(), total, progress, ct);
            if (exit != 0) throw new Exception($"ffmpeg merge (re-encode) failed with exit code {exit}");
        }
		
		private void LstMerge_DragEnter(object? sender, DragEventArgs e)
		{
			if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
			{
				var anyVideo = GetDroppedPaths(e).Any(IsVideoFile);
				e.Effect = anyVideo ? DragDropEffects.Copy : DragDropEffects.None;
			}
			else
			{
				e.Effect = DragDropEffects.None;
			}
		}

		private void LstMerge_DragDrop(object? sender, DragEventArgs e)
		{
			if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;

			bool wasEmpty = lstMerge.Items.Count == 0;

			var toAdd = GetDroppedPaths(e)
				.Where(IsVideoFile)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (wasEmpty && toAdd.Count > 0)
				SetDefaultMergeOutputIfFirst(toAdd[0]);

			foreach (var p in toAdd)
			{
				if (!lstMerge.Items.Contains(p))
					lstMerge.Items.Add(p);
			}
		}


		// --- Helpers ---
		private static IEnumerable<string> GetDroppedPaths(DragEventArgs e)
		{
			var data = e.Data?.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
			foreach (var path in data)
			{
				if (Directory.Exists(path))
				{
					// include videos from dropped folders (recursive)
					foreach (var f in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
						yield return f;
				}
				else
				{
					yield return path;
				}
			}
		}

		private static readonly HashSet<string> _videoExts = new(StringComparer.OrdinalIgnoreCase)
		{
			".mp4", ".mkv", ".mov", ".m4v", ".avi", ".ts", ".mts", ".m2ts", ".webm"
		};

		private static bool IsVideoFile(string path)
			=> File.Exists(path) && _videoExts.Contains(Path.GetExtension(path));
			
		private void SetDefaultMergeOutputIfFirst(string samplePath)
		{
			if (string.IsNullOrWhiteSpace(samplePath)) return;
			if (!string.IsNullOrWhiteSpace(txtMergeOut.Text)) return;   // don't overwrite if user already set it
			if (lstMerge.Items.Count > 0) return;                       // only when adding the very first item

			var dir = Path.GetDirectoryName(samplePath);
			if (string.IsNullOrEmpty(dir)) return;

			txtMergeOut.Text = Path.Combine(dir, "merged_output.mkv");
		}
		
		private void TxtInput_DragEnter(object? sender, DragEventArgs e)
		{
			if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
			{
				var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
				bool ok = paths.Any(p => File.Exists(p) && IsVideoFile(p));
				e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
			}
			else
			{
				e.Effect = DragDropEffects.None;
			}
		}

		private void TxtInput_DragDrop(object? sender, DragEventArgs e)
		{
			if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;

			var paths = (e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>())
						.Where(p => File.Exists(p) && IsVideoFile(p))
						.ToList();
			if (paths.Count == 0) return;

			// Use the first video file dropped
			var input = paths[0];
			txtInput.Text = input;

			// If output is empty, set default: same folder + "_cut" + same extension
			if (string.IsNullOrWhiteSpace(txtOutput.Text))
				txtOutput.Text = BuildDefaultCutOutputPath(input);
		}

		// Helper: build "<dir>\<name>_cut<ext>"
		private static string BuildDefaultCutOutputPath(string inputPath)
		{
			var dir = Path.GetDirectoryName(inputPath) ?? "";
			var name = Path.GetFileNameWithoutExtension(inputPath);
			var ext = Path.GetExtension(inputPath);
			return Path.Combine(dir, $"{name}_cut{ext}");
		}
		
		private void TxtInput_TextChanged(object? sender, EventArgs e)
		{
			var input = txtInput.Text;
			if (string.IsNullOrWhiteSpace(input)) return;

			// If user hasn't customized output OR it's empty, keep auto-updating
			if (_cutOutputIsAuto || string.IsNullOrWhiteSpace(txtOutput.Text))
			{
				SetCutOutputFromInput(input);
			}
		}

		private void TxtOutput_TextChanged(object? sender, EventArgs e)
		{
			// If we changed it programmatically, ignore this change
			if (_suppressOutputChanged) return;

			// User typed or chose a custom output -> stop auto updates
			_cutOutputIsAuto = false;
		}

		private void SetCutOutputFromInput(string inputPath)
		{
			var autoPath = BuildDefaultCutOutputPath(inputPath);
			_suppressOutputChanged = true;
			try
			{
				txtOutput.Text = autoPath;
				_cutOutputIsAuto = true; // still auto as long as user hasn't typed
			}
			finally
			{
				_suppressOutputChanged = false;
			}
		}

    }

    // ---------------- Runner ----------------
    public sealed class FfmpegRunner
    {
        public string FfmpegPath { get; }
        public string FfprobePath { get; }

        public FfmpegRunner(string ffmpegPath, string ffprobePath)
        {
            FfmpegPath = ffmpegPath;
            FfprobePath = ffprobePath;
            if (!File.Exists(FfmpegPath)) throw new FileNotFoundException("ffmpeg.exe not found", FfmpegPath);
            if (!File.Exists(FfprobePath)) throw new FileNotFoundException("ffprobe.exe not found", FfprobePath);
        }

        public async Task<double?> GetDurationSecondsAsync(string input, CancellationToken ct = default)
        {
            var args = $"-v error -show_entries format=duration -of default=nk=1:nw=1 {Q(input)}";
            var (exit, stdout, _) = await RunProcessAsync(FfprobePath, args, null, ct);
            if (exit == 0 && double.TryParse(stdout.Trim().Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var dur))
                return dur;
            return null;
        }

        public async Task<int> RunFfmpegAsync(string arguments, double? totalSeconds = null,
                                              IProgress<double>? progress = null,
                                              CancellationToken ct = default)
        {
            // Parse time=hh:mm:ss.xx from stderr
            var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2}\.\d+)", RegexOptions.Compiled);

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>();

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                if (totalSeconds is > 0)
                {
                    var m = timeRegex.Match(e.Data);
                    if (m.Success)
                    {
                        int hh = int.Parse(m.Groups[1].Value);
                        int mm = int.Parse(m.Groups[2].Value);
                        double ss = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var secs = hh * 3600 + mm * 60 + ss;
                        progress?.Report(Math.Clamp(secs / totalSeconds.Value, 0, 1));
                    }
                }
            };
            proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);

            proc.Start();
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            using (ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
                tcs.TrySetCanceled(ct);
            }))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
            string fileName, string arguments, string? workingDir, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir ?? "",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var p = new Process { StartInfo = psi };
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            var tcs = new TaskCompletionSource<int>();
            p.EnableRaisingEvents = true;
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using (ct.Register(() =>
            {
                try { if (!p.HasExited) p.Kill(true); } catch { }
                tcs.TrySetCanceled(ct);
            }))
            {
                var exit = await tcs.Task.ConfigureAwait(false);
                return (exit, sbOut.ToString(), sbErr.ToString());
            }
        }

        public static string Q(string path) => $"\"{path}\"";
    }
}
