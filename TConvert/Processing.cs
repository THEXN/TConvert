using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml;
using TConvert.Convert;
using TConvert.Extract;
using TConvert.Util;
#if !(CONSOLE)
using TConvert.Windows;
#endif

namespace TConvert {
	/**<summary>A log error.</summary>*/
	public struct LogError {
		/**<summary>True if the log item is a warning and not an error.</summary>*/
		public bool IsWarning;
		/**<summary>The log message.</summary>*/
		public string Message;
		/**<summary>The error/warning reason.</summary>*/
		public string Reason;
		/**<summary>Constructs a log error.</summary>*/
		public LogError(bool isWarning, string message, string reason) {
			IsWarning = isWarning;
			Message = message;
			Reason = reason;
		}
	}
	/**<summary>A pair of input and output paths.</summary>*/
	public struct PathPair {
		/**<summary>The input path.</summary>*/
		public string Input;
		/**<summary>The output path.</summary>*/
		public string Output;
		/**<summary>True if compression should be used.</summary>*/
		public bool Compress;
		/**<summary>True if alpha is premultiplied.</summary>*/
		public bool Premultiply;
		/**<summary>Constructs a path pair.</summary>*/
		public PathPair(string input, string output) {
			Input = input;
			Output = output;
			Compress = false;
			Premultiply = true;
		}
		/**<summary>Constructs a path pair.</summary>*/
		public PathPair(string input, string output, bool compress, bool premultiply) {
			Input = input;
			Output = output;
			Compress = compress;
			Premultiply = premultiply;
		}
	}
	/**<summary>A loaded script.</summary>*/
	public class Script {
		/**<summary>The list of backup directories.</summary>*/
		public List<PathPair> Backups;
		/**<summary>The list of restore directories.</summary>*/
		public List<PathPair> Restores;
		/**<summary>The list of extract files.</summary>*/
		public List<PathPair> Extracts;
		/**<summary>The list of convert files.</summary>*/
		public List<PathPair> Converts;
	}
	/**<summary>The process modes available.</summary>*/
	public enum ProcessModes {
		Any,
		Extract,
		Convert,
		Backup,
		Restore,
		Script
	}
	/**<summary>Processes file requests.</summary>*/
	public static class Processing {
		//========== CONSTANTS ===========
		#region Constants

		/**<summary>The duration before updating the progress again.</summary>*/
		private static readonly TimeSpan UpdateSpan = TimeSpan.FromMilliseconds(50);

		#endregion
		//=========== MEMBERS ============
		#region Members
		//--------------------------------
		#region Processing

		/**<summary>The lasy time the progress was updated.</summary>*/
		private static DateTime lastUpdate = DateTime.MinValue;
		/**<summary>The total number of files to process.</summary>*/
		private static int totalFiles = 0;
		/**<summary>The number of files completed.</summary>*/
		private static int filesCompleted = 0;
		/**<summary>The list of errors and warnings that occurred.</summary>*/
		private static List<LogError> errorLog = new List<LogError>();
		/**<summary>True if an error occurred.</summary>*/
		//private static bool errorOccurred = false;
		/**<summary>True if an warning occurred.</summary>*/
		//private static bool warningOccurred = false;
		/**<summary>True if images should be compressed.</summary>*/
		private static bool compressImages = true;
		/**<summary>True if a sound is played upon completion.</summary>*/
		private static bool completionSound;
		/**<summary>True if alpha is premultiplied when converting back to xnb.</summary>*/
		private static bool premultiplyAlpha = true;

		#endregion
		//--------------------------------
		#region Console Only

		/**<summary>The starting X position of the console output.</summary>*/
		private static int consoleX;
		/**<summary>The starting Y position of the console output.</summary>*/
		private static int consoleY;
		/**<summary>True if there's no console output.</summary>*/
		private static bool silent;
		/**<summary>The start time of the console operation.</summary>*/
		private static DateTime startTime;

		#endregion
		//--------------------------------
		#region Window Only

#if !(CONSOLE)
		private static ProgressWindow progressWindow;
		private static bool autoCloseProgress;
		private static bool console = false;
#endif

		#endregion
		//--------------------------------
		#endregion
		//=========== STARTING ===========
		#region Starting

#if !(CONSOLE)
		/**<summary>Starts a progress window processing thread.</summary>*/
		public static void StartProgressThread(Window owner, string message, bool autoClose, bool compress, bool sound, bool premultiply, Thread thread) {
			console = false;
			compressImages = compress;
			premultiplyAlpha = premultiply;
			completionSound = sound;
			lastUpdate = DateTime.MinValue;
			autoCloseProgress = autoClose;
			filesCompleted = 0;
			totalFiles = 0;
			errorLog.Clear();
			progressWindow = new ProgressWindow(thread, OnProgressCancel);
			if (owner != null) {
				progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
				progressWindow.Owner = owner;
			}
			if (Application.Current.MainWindow == null)
				Application.Current.MainWindow = progressWindow;
			// Prevent Explorer from freazing until the progress window is closed
			Thread showThread = new Thread(() => {
				Application.Current.Dispatcher.Invoke(() => {
					progressWindow.ShowDialog();
				});
			});
			showThread.Start();
		}
#endif
		/**<summary>Starts a console processing thread.</summary>*/
		public static void StartConsoleThread(string message, bool silent, bool compress, bool sound, bool premultiply, Thread thread) {
			#if !(CONSOLE)
			console = true;
			#endif
			compressImages = compress;
			premultiplyAlpha = premultiply;
			completionSound = sound;
			Processing.silent = silent;
			startTime = DateTime.Now;
			lastUpdate = DateTime.MinValue;
			filesCompleted = 0;
			totalFiles = 0;
			errorLog.Clear();
			consoleX = Console.CursorLeft;
			consoleY = Console.CursorTop;
			WriteTimeAndPercentage(message);
			thread.Start();
			// Wait for the thread to finish
			thread.Join();
			if (!silent)
				Console.WriteLine();
			#if !(CONSOLE)
            Console.Write("按回车继续...");

			#endif
        }

        #endregion
        //=========== PROGRESS ===========
        #region Progress

        /**<summary>Updates the progress on the console.</summary>*/
        private static void WriteTimeAndPercentage(string message, bool finished = false) {
			if (!silent) {
				// Prepare to overwrite the leftover message
				int oldX = Console.CursorLeft;
				int oldY = Console.CursorTop;
				int oldXY = oldY * Console.BufferWidth + oldX;
				if (!Console.IsOutputRedirected) {
					Console.SetCursorPosition(consoleX, consoleY);
				}

                string timeStr = (finished ? "总计 " : "");
                timeStr += "时间: " + (DateTime.Now - startTime).ToString(@"m\:ss");
                timeStr += " (" + (int)(totalFiles == 0 ? 0 : ((double)filesCompleted / totalFiles * 100)) + "%)";
                timeStr += "      ";
                Console.WriteLine(timeStr);
				Console.Write(message);

				// Overwrite the leftover message
				if (!Console.IsOutputRedirected) {
					int newX = Console.CursorLeft;
					int newY = Console.CursorTop;
					int newXY = newY * Console.BufferWidth + newX;
					if (newXY < oldXY) {
						Console.Write(new string(' ',
							(oldY - newY) * Console.BufferWidth + (oldX - newX)
						));
					}
				}
			}
		}
		/**<summary>Called when the progress window is canceled.</summary>*/
		private static void OnProgressCancel() {
			Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			ErrorLogger.Close();
			if (errorLog.Count > 0) {
				#if !(CONSOLE)
				progressWindow = null;
				#endif
				ShowErrorLog();
				errorLog.Clear();
			}
		}
		/**<summary>Called to update the progress with a message.</summary>*/
		public static void UpdateProgress(string message, bool forceUpdate = false) {
			#if !(CONSOLE)
			if (progressWindow != null) {
				if (lastUpdate + UpdateSpan < DateTime.Now || forceUpdate) {
					progressWindow.Dispatcher.Invoke(() => {
						progressWindow.Update(message, totalFiles == 0 ? 0 : ((double)filesCompleted / totalFiles));
					});
					lastUpdate = DateTime.Now;
				}
			}
			else if (console)
			#endif
			{
				if (lastUpdate + UpdateSpan < DateTime.Now || forceUpdate) {
					WriteTimeAndPercentage(message);
					lastUpdate = DateTime.Now;
				}
			}
		}
		/**<summary>Called to finish the progress with a message.</summary>*/
		public static void FinishProgress(string message) {
			Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			#if !(CONSOLE)
			if (progressWindow != null) {
				progressWindow.Dispatcher.Invoke(() => {
					progressWindow.Finish(message, errorLog.Count > 0);
				});
				ErrorLogger.Close();
				if (errorLog.Count > 0) {
					if (completionSound)
						SystemSounds.Exclamation.Play();
					ShowErrorLog();
					errorLog.Clear();
				}
				else if (completionSound) {
					SystemSounds.Asterisk.Play();
				}
				if (autoCloseProgress) {
					progressWindow.Dispatcher.Invoke(() => {
						progressWindow.Close();
					});
				}
			}
			else if (console)
			#endif
			{
				WriteTimeAndPercentage(message);
				ErrorLogger.Close();
				if (errorLog.Count > 0) {
					ShowErrorLog();
					errorLog.Clear();
				}
			}
		}
		/**<summary>Shows the error log if necissary.</summary>*/
		public static void ShowErrorLog() {
			#if !(CONSOLE)
			if (!console) {
				App.Current.Dispatcher.Invoke(() => {
					DispatcherObject dispatcher;
					Window window = null;
					if (progressWindow != null && !autoCloseProgress) {
						window = progressWindow;
						dispatcher = window;
					}
					else if (App.Current.MainWindow != null) {
						window = App.Current.MainWindow;
						dispatcher = window;
					}
					else {
						dispatcher = Application.Current;
					}
					ErrorLogWindow.Show(window, errorLog.ToArray());
					errorLog.Clear();
				});
			}
			else
			#endif
			{
				if (!silent) {
					ConsoleColor oldColor = Console.ForegroundColor;
					Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("在过程中遇到错误或警告。\n请查看 '" + Path.GetFileName(ErrorLogger.LogPath) + "' 以获取更多详情。");
                    Console.ForegroundColor = oldColor;
				}
			}
		}
		/**<summary>Logs an error.</summary>*/
		public static void LogError(string message, string reason = "") {
			errorLog.Add(new LogError(false, message, reason));
            ErrorLogger.WriteLine("错误: " + message);
            if (reason != String.Empty)
                ErrorLogger.WriteLine("    原因: " + reason);
        }
        /**<summary>Logs a warning.</summary>*/
        public static void LogWarning(string message, string reason = "") {
			errorLog.Add(new LogError(true, message, reason));
            ErrorLogger.WriteLine("警告: " + message);
            if (reason != String.Empty)
                ErrorLogger.WriteLine("    原因: " + reason);
        }

        #endregion
        //========== PROCESSING ==========
        #region Processing


        /**<summary>Checks if the extension is that of an audio file.</summary>*/
        public static bool IsAudioExtension(string ext) {
			switch (ext) {
			case ".wav":
			case ".mp3":
			case ".mp2":
			case ".mpga":
			case ".m4a":
			case ".aac":
			case ".flac":
			case ".ogg":
			case ".wma":
			case ".aif":
			case ".aiff":
			case ".aifc":
				return true;
			}
			return false;
		}
		/**<summary>Processes drop files.</summary>*/
		public static void ProcessDropFiles(string[] extractFiles, string[] convertFiles, string[] scriptFiles) {
			List<Script> scripts = new List<Script>();

			foreach (string scriptFile in scriptFiles) {
				Script script = LoadScript(scriptFile);
				if (script != null) {
					scripts.Add(script);
					totalFiles += script.Extracts.Count + script.Converts.Count;
					foreach (PathPair backup in script.Backups) {
						totalFiles += Helpers.GetFileCount(backup.Input);
					}
					foreach (PathPair restore in script.Restores) {
						totalFiles += Helpers.GetFileCount(restore.Input);
					}
				}
			}
			totalFiles += extractFiles.Length + convertFiles.Length;

			if (extractFiles.Length != 0)
				ExtractDropFiles(extractFiles);
			if (convertFiles.Length != 0)
				ConvertDropFiles(convertFiles);
			foreach (Script script in scripts) {
				RunScript(script, false);
			}
            FinishProgress("文件处理完成");
        }
        /**<summary>Processes console files.</summary>*/
        public static void ProcessFiles(ProcessModes mode, string[] inputFiles, string[] outputFiles) {
			List<PathPair> files = new List<PathPair>();

			// Allow processing of directories too
			for (int i = 0; i < inputFiles.Length; i++) {
				string input = inputFiles[i];
				string output = outputFiles[i];
				if (Directory.Exists(input)) {
					string[] dirFiles = Helpers.FindAllFiles(input);
					foreach (string dirFile in dirFiles) {
						files.Add(new PathPair(dirFile, Helpers.GetOutputPath(dirFile, input, output)));
					}
				}
				else {
					files.Add(new PathPair(input, output));
				}
			}

			if (mode != ProcessModes.Backup && mode != ProcessModes.Restore) {
				List<PathPair> extractFiles = new List<PathPair>();
				List<PathPair> convertFiles = new List<PathPair>();
				List<string> scriptFiles = new List<string>();

				foreach (PathPair pair in files) {
					string ext = Path.GetExtension(pair.Input).ToLower();
					switch (ext) {
					case ".xnb":
					case ".xwb":
						if (mode == ProcessModes.Any || mode == ProcessModes.Extract)
							extractFiles.Add(pair);
						break;
					case ".png":
					case ".bmp":
					case ".jpg":
						if (mode == ProcessModes.Any || mode == ProcessModes.Convert)
							convertFiles.Add(pair);
						break;
					case ".xml":
						if (mode == ProcessModes.Any || mode == ProcessModes.Script)
							scriptFiles.Add(pair.Input);
						break;
					default:
						if (IsAudioExtension(ext) && (mode == ProcessModes.Any || mode == ProcessModes.Convert))
							convertFiles.Add(pair);
						break;
					}
				}
				
				List<Script> scripts = new List<Script>();
				foreach (string scriptFile in scriptFiles) {
					Script script = LoadScript(scriptFile);
					if (script != null) {
						scripts.Add(script);
						totalFiles += script.Extracts.Count + script.Converts.Count;
						foreach (PathPair backup in script.Backups) {
							totalFiles += Helpers.GetFileCount(backup.Input);
						}
						foreach (PathPair restore in script.Restores) {
							totalFiles += Helpers.GetFileCount(restore.Input);
						}
					}
				}
				totalFiles += extractFiles.Count + convertFiles.Count;

				foreach (var pair in extractFiles) {
					ExtractFile2(pair.Input, pair.Output);
				}
				foreach (var pair in convertFiles) {
					ConvertFile2(pair.Input, pair.Output, compressImages);
				}
				foreach (Script script in scripts) {
					RunScript(script, false);
				}
			}
			else if (mode == ProcessModes.Backup) {
				foreach (var pair in files) {
					BackupFile2(pair.Input, pair.Output);
				}
			}
			else if (mode == ProcessModes.Restore) {
				foreach (var pair in files) {
					RestoreFile2(pair.Input, pair.Output);
				}
			}

            FinishProgress("文件处理完成");
        }

        #endregion
        //========== EXTRACTING ==========
        #region Extracting

        /**<summary>Extracts all files in a directory.</summary>*/
        public static void ExtractAll(string inputDirectory, string outputDirectory, bool includeImages, bool includeSounds, bool includeFonts, bool includeWaveBank) {
			string[] files = Helpers.FindAllFiles(inputDirectory);
			totalFiles += files.Length;

			int extractCount = 0;
			foreach (string inputFile in files) {
				if (ExtractFile(inputFile, inputDirectory, outputDirectory, includeImages, includeSounds, includeFonts, includeWaveBank))
					extractCount++;
			}

            FinishProgress("提取了 " + extractCount + " 个文件");
        }
        /**<summary>Extracts a single file.</summary>*/
        public static void ExtractSingleFile(string inputFile, string outputFile) {
			totalFiles += 1;
			
			ExtractFile(inputFile, Path.GetDirectoryName(inputFile), Path.GetDirectoryName(outputFile));

            FinishProgress("提取完成");
        }
        /**<summary>Extracts drop files.</summary>*/
        private static void ExtractDropFiles(string[] inputFiles) {

			foreach (string inputFile in inputFiles) {
				string inputDirectory = Path.GetDirectoryName(inputFile);
				ExtractFile(inputFile, inputDirectory, inputDirectory);
			}

            UpdateProgress("提取完成", true);
        }
        /**<summary>Extracts a file.</summary>*/
        private static bool ExtractFile(string inputFile, string inputDirectory, string outputDirectory, bool includeImages = true, bool includeSounds = true, bool includeFonts = true, bool includeWaveBank = true) {
			bool extracted = false;
			try {
				string outputFile = Helpers.GetOutputPath(inputFile, inputDirectory, outputDirectory);
				string ext = Path.GetExtension(inputFile).ToLower();
				if ((ext == ".xnb" && (includeImages || includeSounds || includeFonts)) || (ext == ".xwb" && includeWaveBank)) {
                    UpdateProgress("正在提取: " + Helpers.GetRelativePath(inputFile, inputDirectory), ext == ".xwb");
                }
                if (ext == ".xnb" && (includeImages || includeSounds || includeFonts)) {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (XnbExtractor.Extract(inputFile, outputFile, true, includeImages, includeSounds, includeFonts))
						extracted = true;
				}
				else if (ext == ".xwb" && includeWaveBank) {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (XactExtractor.Extract(inputFile, Path.GetDirectoryName(outputFile)))
						extracted = true;
				}
			}
            catch (UnauthorizedAccessException ex)
            {
                LogError("正在提取: " + inputFile, "未授权访问 (" + ex.Message + ")");
            }
            catch (FileNotFoundException ex)
            {
                LogError("正在提取: " + inputFile, "文件未找到 (" + ex.Message + ")");
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("正在提取: " + inputFile, "目录未找到 (" + ex.Message + ")");
            }
            catch (IOException ex)
            {
                LogError("正在提取: " + inputFile, "IO 错误 (" + ex.Message + ")");
            }
            catch (XnbException ex)
            {
                LogError("正在提取: " + inputFile, "Xnb 错误 (" + ex.Message + ")");
            }

            catch (ThreadAbortException) { }
			catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("正在提取: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");
            }

            filesCompleted++;
			return extracted;
		}
		/**<summary>Extracts a file with different parameters.</summary>*/
		private static bool ExtractFile2(string inputFile, string outputFile) {
			bool extracted = false;
			try {
				string ext = Path.GetExtension(inputFile).ToLower();
				if (ext == ".xnb" || ext == ".xwb") {
                    UpdateProgress("正在提取: " + Path.GetFileName(inputFile), ext == ".xwb");
                }
                if (ext == ".xnb") {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (XnbExtractor.Extract(inputFile, outputFile, true, true, true, true))
						extracted = true;
				}
				else if (ext == ".xwb") {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (XactExtractor.Extract(inputFile, Path.GetDirectoryName(outputFile)))
						extracted = true;
				}
			}
            catch (UnauthorizedAccessException ex)
            {
                LogError("提取: " + inputFile, "未经授权的访问 (" + ex.Message + ")");
            }
            catch (FileNotFoundException ex)
            {
                LogError("提取: " + inputFile, "文件未找到 (" + ex.Message + ")");
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("提取: " + inputFile, "目录未找到 (" + ex.Message + ")");
            }
            catch (IOException ex)
            {
                LogError("提取: " + inputFile, "IO 错误 (" + ex.Message + ")");
            }
            catch (XnbException ex)
            {
                LogError("提取: " + inputFile, "Xnb 错误 (" + ex.Message + ")");
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("提取: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");
            }

            filesCompleted++;
			return extracted;
		}

        #endregion
        //========== CONVERTING ==========
        #region Converting

        /**<summary>Converts all files in a directory.</summary>*/
        public static void ConvertAll(string inputDirectory, string outputDirectory, bool includeImages, bool includeSounds)
        {
            string[] files = Helpers.FindAllFiles(inputDirectory);
            totalFiles += files.Length;

            int convertCount = 0;
            foreach (string inputFile in files)
            {
                if (ConvertFile(inputFile, inputDirectory, outputDirectory, includeImages, includeSounds))
                    convertCount++;
            }

            FinishProgress("已完成转换 " + convertCount + " 个文件");
        }

        /**<summary>转换单个文件。</summary>*/
        public static void ConvertSingleFile(string inputFile, string outputFile)
        {
            totalFiles += 1;

            ConvertFile(inputFile, Path.GetDirectoryName(inputFile), Path.GetDirectoryName(outputFile));

            FinishProgress("已完成转换");
        }

        /**<summary>转换拖放的文件。</summary>*/
        private static void ConvertDropFiles(string[] inputFiles)
        {
            foreach (string inputFile in inputFiles)
            {
                string inputDirectory = Path.GetDirectoryName(inputFile);
                ConvertFile(inputFile, inputDirectory, inputDirectory);
            }

            UpdateProgress("已完成转换", true);
        }

        /**<summary>Converts a file.</summary>*/
        private static bool ConvertFile(string inputFile, string inputDirectory, string outputDirectory, bool includeImages = true, bool includeSounds = true) {
			bool converted = false;
			try {
				string outputFile = Helpers.GetOutputPath(inputFile, inputDirectory, outputDirectory);
				string ext = Path.GetExtension(inputFile).ToLower();
				if (((ext == ".png" || ext == ".bmp" || ext == ".jpg") && includeImages) || (IsAudioExtension(ext) && includeSounds)) {
					UpdateProgress("Converting: " + Helpers.GetRelativePath(inputFile, inputDirectory));
				}
				if ((ext == ".png" || ext == ".bmp" || ext == ".jpg") && includeImages) {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (PngConverter.Convert(inputFile, outputFile, true, compressImages, true, premultiplyAlpha))
						converted = true;
				}
				else if (IsAudioExtension(ext) && includeSounds) {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (WavConverter.Convert(inputFile, outputFile, true))
						converted = true;
				}
			}
            catch (UnauthorizedAccessException ex)
            {
                LogError("转换: " + inputFile, "未经授权的访问 (" + ex.Message + ")");
            }
            catch (FileNotFoundException ex)
            {
                LogError("转换: " + inputFile, "文件未找到 (" + ex.Message + ")");
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("转换: " + inputFile, "目录未找到 (" + ex.Message + ")");
            }
            catch (IOException ex)
            {
                LogError("转换: " + inputFile, "IO 错误 (" + ex.Message + ")");
            }
            catch (PngException ex)
            {
                LogError("转换: " + inputFile, "Png 错误 (" + ex.Message + ")");
            }
            catch (WavException ex)
            {
                LogError("转换: " + inputFile, "Wav 错误 (" + ex.Message + ")");
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("转换: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");
            }

            filesCompleted++;
			return converted;
		}
		/**<summary>Converts a file with different paramters.</summary>*/
		private static bool ConvertFile2(string inputFile, string outputFile, bool compress) {
			bool converted = false;
			try {
				string ext = Path.GetExtension(inputFile).ToLower();
				if (ext == ".png" || ext == ".bmp" || ext == ".jpg" || IsAudioExtension(ext)) {
					UpdateProgress("Converting: " + Path.GetFileName(inputFile));
				}
				if (ext == ".png" || ext == ".bmp" || ext == ".jpg") {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (PngConverter.Convert(inputFile, outputFile, true, compress, true, premultiplyAlpha))
						converted = true;
				}
				else if (IsAudioExtension(ext)) {
					Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));
					if (WavConverter.Convert(inputFile, outputFile, true))
						converted = true;
				}
			}
            catch (UnauthorizedAccessException ex)
            {
                LogError("转换: " + inputFile, "未经授权的访问 (" + ex.Message + ")");
            }
            catch (FileNotFoundException ex)
            {
                LogError("转换: " + inputFile, "文件未找到 (" + ex.Message + ")");
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("转换: " + inputFile, "目录未找到 (" + ex.Message + ")");
            }
            catch (IOException ex)
            {
                LogError("转换: " + inputFile, "IO 错误 (" + ex.Message + ")");
            }
            catch (PngException ex)
            {
                LogError("转换: " + inputFile, "Png 错误 (" + ex.Message + ")");
            }
            catch (WavException ex)
            {
                LogError("转换: " + inputFile, "Wav 错误 (" + ex.Message + ")");
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("转换: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");
            }

            filesCompleted++;
			return converted;
		}

		#endregion
		//========== SCRIPTING ===========
		#region Scripting

		/**<summary>Loads and runs a script.</summary>*/
		public static void RunScript(string inputScript) {
            UpdateProgress("加载脚本...", true);

            Script script = LoadScript(inputScript);

			// Add up all the files and restores
			totalFiles += script.Extracts.Count + script.Converts.Count;
			foreach (PathPair backup in script.Backups) {
				totalFiles += Helpers.GetFileCount(backup.Input);
			}
			foreach (PathPair restore in script.Restores) {
				totalFiles += Helpers.GetFileCount(restore.Input);
			}

			if (script != null) {
				RunScript(script);
			}
			else {
                FinishProgress("脚本完成");
            }
        }
		/**<summary>Runs a a preloaded script.</summary>*/
		public static void RunScript(Script script, bool final = true) {

			int backupCount = 0;
			foreach (PathPair backup in script.Backups) {
				if (!Directory.Exists(backup.Input)) {
                    LogError("备份: " + backup.Input, "目录不存在");
                    continue;
				}
				string[] backupFiles = Helpers.FindAllFiles(backup.Input);

				foreach (string inputFile in backupFiles) {
					if (BackupFile(inputFile, backup.Input, backup.Output))
						backupCount++;
				}
			}

			int restoreCount = 0;
			foreach (PathPair restore in script.Restores) {
				if (!Directory.Exists(restore.Input)) {
                    LogError("恢复: " + restore.Input, "目录不存在");
                    continue;
				}
				string[] restoreFiles = Helpers.FindAllFiles(restore.Input);
				
				foreach (string inputFile in restoreFiles) {
					if (RestoreFile(inputFile, restore.Input, restore.Output))
						restoreCount++;
				}
			}

			int extractCount = 0;
			foreach (PathPair file in script.Extracts) {
				if (ExtractFile2(file.Input, file.Output))
					extractCount++;
			}

			int convertCount = 0;
			foreach (PathPair file in script.Converts) {
				if (ConvertFile2(file.Input, file.Output, file.Compress))
					convertCount++;
			}
            string message = "完成 ";
            if (extractCount > 0)
            {
                if (convertCount > 0)
                    message += "提取和转换 " + (extractCount + convertCount) + " 个文件";
                else
                    message += "提取 " + extractCount + " 个文件";
            }
            else if (convertCount > 0)
                message += "转换 " + convertCount + " 个文件";
            else if (restoreCount > 0)
                message += "恢复 " + restoreCount + " 个文件";
            else if (backupCount > 0)
                message += "备份 " + backupCount + " 个文件";
            else
                message += "脚本";

            if (final)
				FinishProgress(message);
			else
				UpdateProgress(message, true);
		}
        /**<summary>加载脚本。</summary>*/
        public static Script LoadScript(string inputScript)
        {
            List<PathPair> files = new List<PathPair>();
            List<PathPair> backups = new List<PathPair>();
            List<PathPair> restores = new List<PathPair>();

            try
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(inputScript));
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("设置工作目录失败", ex.Message);
                FinishProgress("完成脚本");
                return null;
            }

            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(inputScript);
            }
            catch (XmlException ex)
            {
                LogError("解析脚本失败: " + inputScript, ex.Message);
                FinishProgress("完成脚本");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("读取脚本: " + inputScript, "未经授权的访问 (" + ex.Message + ")");
                FinishProgress("完成脚本");
                return null;
            }
            catch (FileNotFoundException ex)
            {
                LogError("读取脚本: " + inputScript, "文件未找到 (" + ex.Message + ")");
                FinishProgress("完成脚本");
                return null;
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("读取脚本: " + inputScript, "目录未找到 (" + ex.Message + ")");
                FinishProgress("完成脚本");
                return null;
            }
            catch (IOException ex)
            {
                LogError("读取脚本: " + inputScript, "IO 错误 (" + ex.Message + ")");
                FinishProgress("完成脚本");
                return null;
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("读取脚本: " + inputScript, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");
                FinishProgress("完成脚本");
                return null;
            }

            XmlElement root = doc["TConvertScript"];

            // 查找所有文件和恢复项
            if (root != null)
            {
                LoadScriptFolder(root, files, backups, restores, "", "", compressImages, premultiplyAlpha, true);
            }
            else
            {
                LogError("读取脚本", "没有根元素 TConvertScript.");
                FinishProgress("完成脚本");
                return null;
            }

            List<PathPair> extracts = new List<PathPair>();
            List<PathPair> converts = new List<PathPair>();
            foreach (PathPair file in files)
            {
                string ext = Path.GetExtension(file.Input).ToLower();
                switch (ext)
                {
                    case ".xnb":
                    case ".xwb":
                        extracts.Add(file); break;
                    case ".png":
                    case ".bmp":
                    case ".jpg":
                        converts.Add(file); break;
                    default:
                        if (IsAudioExtension(ext))
                            converts.Add(file);
                        break;
                }
            }

            return new Script { Extracts = extracts, Converts = converts, Backups = backups, Restores = restores };
        }

        /**<summary>加载脚本文件夹或根元素。</summary>*/
        private static void LoadScriptFolder(XmlElement element, List<PathPair> files, List<PathPair> backups, List<PathPair> restores, string output, string path, bool compress, bool premultiply, bool isRoot = false)
        {
            string newOutput = output;
            bool newCompress = compress;
            bool newPremultiply = premultiply;
            XmlAttribute attribute;
            foreach (XmlNode nextNode in element)
            {
                XmlElement next = nextNode as XmlElement;
                if (next == null)
                    continue;

                switch (next.Name)
                {
                    case "Compress":
                        // 处理 "Compress" 元素
                        attribute = next.Attributes["Value"];
                        if (attribute != null)
                        {
                            bool nextCompress;
                            if (bool.TryParse(attribute.InnerText, out nextCompress))
                                newCompress = nextCompress;
                            else
                                LogWarning("读取脚本", "无法解析 Compress 元素中的 Value 属性: '" + attribute.InnerText + "'。");
                        }
                        else
                        {
                            LogWarning("读取脚本", "Compress 元素中没有 Value 属性。");
                        }
                        break;
                    case "Premultiply":
                        // 处理 "Premultiply" 元素
                        attribute = next.Attributes["Value"];
                        if (attribute != null)
                        {
                            bool nextPremultiply;
                            if (bool.TryParse(attribute.InnerText, out nextPremultiply))
                                newPremultiply = nextPremultiply;
                            else
                                LogWarning("读取脚本", "无法解析 Premultiply 元素中的 Value 属性: '" + attribute.InnerText + "'。");
                        }
                        else
                        {
                            LogWarning("读取脚本", "Premultiply 元素中没有 Value 属性。");
                        }
                        break;
                    case "Backup":
                        // 处理 "Backup" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                string nextPath;
                                if (path == string.Empty)
                                    nextPath = attribute.InnerText;
                                else
                                    nextPath = Path.Combine(path, attribute.InnerText);
                                backups.Add(new PathPair(nextPath, newOutput));
                            }
                            else
                            {
                                LogWarning("读取脚本", "Backup 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Backup 元素中没有 Path 属性。");
                        }
                        break;
                    case "Restore":
                        // 处理 "Restore" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                string nextPath;
                                if (path == string.Empty)
                                    nextPath = attribute.InnerText;
                                else
                                    nextPath = Path.Combine(path, attribute.InnerText);
                                restores.Add(new PathPair(nextPath, newOutput));
                            }
                            else
                            {
                                LogWarning("读取脚本", "Restore 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Restore 元素中没有 Path 属性。");
                        }
                        break;
                    case "Output":
                        // 处理 "Output" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                if (output == string.Empty)
                                    newOutput = attribute.InnerText;
                                else
                                    newOutput = Path.Combine(output, attribute.InnerText);
                            }
                            else
                            {
                                LogWarning("读取脚本", "Output 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Output 元素中没有 Path 属性。");
                        }
                        break;
                    case "Folder":
                        // 处理 "Folder" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                string nextPath;
                                if (path == string.Empty)
                                    nextPath = attribute.InnerText;
                                else
                                    nextPath = Path.Combine(path, attribute.InnerText);
                                LoadScriptFolder(next, files, backups, restores, newOutput, nextPath, newCompress, newPremultiply);
                            }
                            else
                            {
                                LogWarning("读取脚本", "Folder 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Folder 元素中没有 Path 属性。");
                        }
                        break;
                    case "File":
                        // 处理 "File" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                string nextPath;
                                bool nextCompress = newCompress;
                                bool nextPremultiply = true;
                                if (path == string.Empty)
                                    nextPath = attribute.InnerText;
                                else
                                    nextPath = Path.Combine(path, attribute.InnerText);
                                attribute = next.Attributes["Compress"];
                                if (attribute != null)
                                {
                                    if (!bool.TryParse(attribute.InnerText, out nextCompress))
                                    {
                                        LogWarning("读取脚本", "无法解析 Compress 元素中的 Compress 属性: '" + attribute.InnerText + "'。");
                                        nextCompress = newCompress;
                                    }
                                }
                                attribute = next.Attributes["Premultiply"];
                                if (attribute != null)
                                {
                                    if (!bool.TryParse(attribute.InnerText, out nextPremultiply))
                                    {
                                        LogWarning("读取脚本", "无法解析 Premultiply 元素中的 Premultiply 属性: '" + attribute.InnerText + "'。");
                                        nextPremultiply = true;
                                    }
                                }
                                attribute = next.Attributes["OutPath"];
                                if (attribute != null)
                                {
                                    if (Helpers.IsPathValid(attribute.InnerText))
                                    {
                                        string nextOutput;
                                        if (newOutput == string.Empty)
                                            nextOutput = Helpers.FixPathSafe(attribute.InnerText);
                                        else
                                            nextOutput = Path.Combine(newOutput, attribute.InnerText);
                                        files.Add(new PathPair(nextPath, nextOutput, nextCompress, nextPremultiply));
                                    }
                                    else
                                    {
                                        LogWarning("读取脚本", "File 元素中的 OutPath 属性无效: '" + attribute.InnerText + "'。");
                                    }
                                }
                                LoadScriptFile(next, files, newOutput, nextPath, nextCompress, nextPremultiply);
                            }
                            else
                            {
                                LogWarning("读取脚本", "File 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "File 元素中没有 Path 属性。");
                        }
                        break;
                    default:
                        LogWarning("读取脚本", "无效的元素出现在 " + (isRoot ? "ConvertScript" : "Folder") + " 中: '" + next.Name + "'。");
                        break;
                }
            }
        }

        /**<summary>加载脚本文件。</summary>*/
        private static void LoadScriptFile(XmlElement element, List<PathPair> files, string output, string path, bool compress, bool premultiply)
        {
            string newOutput = output;
            bool newCompress = compress;
            bool newPremultiply = premultiply;
            XmlAttribute attribute;

            // 遍历所有子元素
            foreach (XmlNode nextNode in element)
            {
                XmlElement next = nextNode as XmlElement;
                if (next == null)
                    continue;

                switch (next.Name)
                {
                    case "Compress":
                        // 处理 "Compress" 元素
                        attribute = next.Attributes["Value"];
                        if (attribute != null)
                        {
                            bool nextCompress;
                            if (bool.TryParse(attribute.InnerText, out nextCompress))
                                newCompress = nextCompress;
                            else
                                LogWarning("读取脚本", "无法解析 Compress 元素中的 Value 属性: '" + attribute.InnerText + "'。");
                        }
                        else
                        {
                            LogWarning("读取脚本", "Compress 元素中没有 Value 属性。");
                        }
                        break;

                    case "Premultiply":
                        // 处理 "Premultiply" 元素
                        attribute = next.Attributes["Value"];
                        if (attribute != null)
                        {
                            bool nextPremultiply;
                            if (bool.TryParse(attribute.InnerText, out nextPremultiply))
                                newPremultiply = nextPremultiply;
                            else
                                LogWarning("读取脚本", "无法解析 Premultiply 元素中的 Value 属性: '" + attribute.InnerText + "'。");
                        }
                        else
                        {
                            LogWarning("读取脚本", "Premultiply 元素中没有 Value 属性。");
                        }
                        break;

                    case "Output":
                        // 处理 "Output" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                if (output == string.Empty)
                                    newOutput = attribute.InnerText;
                                else
                                    newOutput = Path.Combine(output, attribute.InnerText);
                            }
                            else
                            {
                                LogWarning("读取脚本", "Output 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Output 元素中没有 Path 属性。");
                        }
                        break;

                    case "Out":
                        // 处理 "Out" 元素
                        attribute = next.Attributes["Path"];
                        if (attribute != null)
                        {
                            if (Helpers.IsPathValid(attribute.InnerText))
                            {
                                string nextOutput;
                                bool nextCompress = newCompress;
                                bool nextPremultiply = newPremultiply;

                                // 如果没有给定输出路径，则使用当前输出路径
                                if (newOutput == string.Empty)
                                    nextOutput = attribute.InnerText;
                                else
                                    nextOutput = Path.Combine(newOutput, attribute.InnerText);

                                // 处理 Compress 和 Premultiply 属性
                                attribute = next.Attributes["Compress"];
                                if (attribute != null)
                                {
                                    if (!bool.TryParse(attribute.InnerText, out nextCompress))
                                    {
                                        LogWarning("读取脚本", "无法解析 Out 元素中的 Compress 属性: '" + attribute.InnerText + "'。");
                                        nextCompress = newCompress;
                                    }
                                }

                                attribute = next.Attributes["Premultiply"];
                                if (attribute != null)
                                {
                                    if (!bool.TryParse(attribute.InnerText, out nextPremultiply))
                                    {
                                        LogWarning("读取脚本", "无法解析 Out 元素中的 Premultiply 属性: '" + attribute.InnerText + "'。");
                                        nextPremultiply = newPremultiply;
                                    }
                                }

                                // 添加新的 PathPair 到文件列表
                                files.Add(new PathPair(path, nextOutput, nextCompress, nextPremultiply));
                            }
                            else
                            {
                                LogWarning("读取脚本", "Out 元素中的 Path 属性无效: '" + attribute.InnerText + "'。");
                            }
                        }
                        else
                        {
                            LogWarning("读取脚本", "Out 元素中没有 Path 属性。");
                        }
                        break;

                    default:
                        // 如果元素名称无效，记录警告
                        LogWarning("读取脚本", "File 元素中包含无效的子元素: '" + next.Name + "'。");
                        break;
                }
            }
        }


        #endregion
        //============ BACKUP ============
        #region Backup

        /**<summary>备份一个目录中的所有文件。</summary>*/
        public static void BackupFiles(string inputDirectory, string outputDirectory)
        {
            string[] files = Helpers.FindAllFiles(inputDirectory);  // 获取输入目录下的所有文件
            totalFiles = files.Length;  // 更新文件总数

            foreach (string inputFile in files)
            {
                BackupFile(inputFile, inputDirectory, outputDirectory);  // 备份每个文件
            }

            FinishProgress("已完成备份 " + files.Length + " 个文件");  // 完成备份后显示进度
        }
        /**<summary>恢复一个目录中的所有文件。</summary>*/
        public static void RestoreFiles(string inputDirectory, string outputDirectory)
        {
            string[] files = Helpers.FindAllFiles(inputDirectory);  // 获取输入目录下的所有文件
            totalFiles = files.Length;  // 更新文件总数

            int restoreCount = 0;  // 计数恢复的文件数量
            foreach (string inputFile in files)
            {
                if (RestoreFile(inputFile, inputDirectory, outputDirectory))  // 尝试恢复每个文件
                    restoreCount++;  // 如果恢复成功，增加计数
            }

            FinishProgress("已完成恢复 " + restoreCount + " 个文件");  // 完成恢复后显示进度
        }

        /**<summary>备份一个文件。</summary>*/
        private static bool BackupFile(string inputFile, string inputDirectory, string outputDirectory)
        {
            bool backedUp = false;  // 定义备份状态变量
            try
            {
                UpdateProgress("正在备份: " + Helpers.GetRelativePath(inputFile, inputDirectory));  // 更新进度

                string outputFile = Helpers.GetOutputPath(inputFile, inputDirectory, outputDirectory);  // 获取目标路径
                Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));  // 创建目标目录（如果不存在）
                File.Copy(inputFile, outputFile, true);  // 复制文件，允许覆盖
                backedUp = true;  // 标记备份成功
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("备份失败: " + inputFile, "未授权访问 (" + ex.Message + ")");  // 错误处理：未授权访问
            }
            catch (FileNotFoundException ex)
            {
                LogError("备份失败: " + inputFile, "文件未找到 (" + ex.Message + ")");  // 错误处理：文件未找到
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("备份失败: " + inputFile, "目录未找到 (" + ex.Message + ")");  // 错误处理：目录未找到
            }
            catch (IOException ex)
            {
                LogError("备份失败: " + inputFile, "IO 错误 (" + ex.Message + ")");  // 错误处理：IO 错误
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("备份失败: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");  // 捕获其他异常
            }
            filesCompleted++;  // 增加已完成文件计数
            return backedUp;  // 返回备份是否成功
        }

        /**<summary>使用不同的参数备份一个文件。</summary>*/
        private static bool BackupFile2(string inputFile, string outputFile)
        {
            bool backedUp = false;  // 定义备份状态变量
            try
            {
                UpdateProgress("正在备份: " + Path.GetFileName(inputFile));  // 更新进度

                Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));  // 创建目标目录（如果不存在）
                File.Copy(inputFile, outputFile, true);  // 复制文件，允许覆盖
                backedUp = true;  // 标记备份成功
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("备份失败: " + inputFile, "未授权访问 (" + ex.Message + ")");  // 错误处理：未授权访问
            }
            catch (FileNotFoundException ex)
            {
                LogError("备份失败: " + inputFile, "文件未找到 (" + ex.Message + ")");  // 错误处理：文件未找到
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("备份失败: " + inputFile, "目录未找到 (" + ex.Message + ")");  // 错误处理：目录未找到
            }
            catch (IOException ex)
            {
                LogError("备份失败: " + inputFile, "IO 错误 (" + ex.Message + ")");  // 错误处理：IO 错误
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("备份失败: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");  // 捕获其他异常
            }
            filesCompleted++;  // 增加已完成文件计数
            return backedUp;  // 返回备份是否成功
        }

        /**<summary>恢复一个文件。</summary>*/
        private static bool RestoreFile(string inputFile, string inputDirectory, string outputDirectory)
        {
            bool filedCopied = false;  // 定义文件复制状态变量
            try
            {
                UpdateProgress("正在恢复: " + Helpers.GetRelativePath(inputFile, inputDirectory));  // 更新进度

                string outputFile = Helpers.GetOutputPath(inputFile, inputDirectory, outputDirectory);  // 获取目标路径
                bool shouldCopy = true;
                Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));  // 创建目标目录（如果不存在）
                if (File.Exists(outputFile))
                {  // 如果目标文件存在，检查文件是否需要复制
                    FileInfo info1 = new FileInfo(inputFile);
                    FileInfo info2 = new FileInfo(outputFile);
                    shouldCopy = info1.LastWriteTime != info2.LastWriteTime || info1.Length != info2.Length;  // 如果文件有更新或大小不同，则需要复制
                }
                if (shouldCopy)
                {
                    File.Copy(inputFile, outputFile, true);  // 复制文件，允许覆盖
                    filedCopied = true;  // 标记文件已复制
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("恢复失败: " + inputFile, "未授权访问 (" + ex.Message + ")");  // 错误处理：未授权访问
            }
            catch (FileNotFoundException ex)
            {
                LogError("恢复失败: " + inputFile, "文件未找到 (" + ex.Message + ")");  // 错误处理：文件未找到
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("恢复失败: " + inputFile, "目录未找到 (" + ex.Message + ")");  // 错误处理：目录未找到
            }
            catch (IOException ex)
            {
                LogError("恢复失败: " + inputFile, "IO 错误 (" + ex.Message + ")");  // 错误处理：IO 错误
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("恢复失败: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");  // 捕获其他异常
            }
            filesCompleted++;  // 增加已完成文件计数
            return filedCopied;  // 返回文件是否成功恢复
        }

        /**<summary>使用不同的参数恢复一个文件。</summary>*/
        private static bool RestoreFile2(string inputFile, string outputFile)
        {
            bool filedCopied = false;  // 定义文件复制状态变量
            try
            {
                UpdateProgress("正在恢复: " + Path.GetFileName(inputFile));  // 更新进度

                bool shouldCopy = true;
                Helpers.CreateDirectorySafe(Path.GetDirectoryName(outputFile));  // 创建目标目录（如果不存在）
                if (File.Exists(outputFile))
                {  // 如果目标文件存在，检查文件是否需要复制
                    FileInfo info1 = new FileInfo(inputFile);
                    FileInfo info2 = new FileInfo(outputFile);
                    shouldCopy = info1.LastWriteTime != info2.LastWriteTime || info1.Length != info2.Length;  // 如果文件有更新或大小不同，则需要复制
                }
                if (shouldCopy)
                {
                    File.Copy(inputFile, outputFile, true);  // 复制文件，允许覆盖
                    filedCopied = true;  // 标记文件已复制
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("恢复失败: " + inputFile, "未授权访问 (" + ex.Message + ")");  // 错误处理：未授权访问
            }
            catch (FileNotFoundException ex)
            {
                LogError("恢复失败: " + inputFile, "文件未找到 (" + ex.Message + ")");  // 错误处理：文件未找到
            }
            catch (DirectoryNotFoundException ex)
            {
                LogError("恢复失败: " + inputFile, "目录未找到 (" + ex.Message + ")");  // 错误处理：目录未找到
            }
            catch (IOException ex)
            {
                LogError("恢复失败: " + inputFile, "IO 错误 (" + ex.Message + ")");  // 错误处理：IO 错误
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                LogError("恢复失败: " + inputFile, ex.GetType().ToString().Split('.').Last() + " (" + ex.Message + ")");  // 捕获其他异常
            }
            filesCompleted++;  // 增加已完成文件计数
            return filedCopied;  // 返回文件是否成功恢复
        }


        #endregion
    }
}
