using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TConvert.Convert;
using TConvert.Properties;

namespace TConvert {
	/**<summary>Handles command line execution.</summary>*/
	public static class CommandLine {
		//=========== CLASSES ============
		#region Classes

		/**<summary>The type of command line options.</summary>*/
		[Flags]
		private enum ArgTypes {
			None		= 0x0,
			Path		= 0x1,
			Input		= 0x2,
			Output		= 0x4,

			// Mode
			Extract		= 0x8,
			Convert		= 0x10,
			Backup		= 0x20,
			Restore		= 0x40,
			Script		= 0x80,

			// Misc
			Help		= 0x100,
			Log			= 0x200,
			Silent		= 0x400,
			AutoClose	= 0x800,
			KeepOpen	= 0x1000,
			Console		= 0x2000,
			//AutoCloseDef= 0x4000,
			//KeepOpenDef = 0x8000,
			Compress	= 0x10000,
			DontCompress= 0x20000,
			Premultiply	= 0x40000,
			DontPremultiply	= 0x80000,
		}

		/**<summary>Information about a command line option.</summary>*/
		private struct OptionInfo {
			public Action Action;
			public string Name;
			public string Description;
			public string[] Options;
			public string PostOptions;
			public OptionInfo(Action action, string name, string description, string postOptions, params string[] options) {
				Action = action;
				Name = name;
				Description = description;
				Options = options;
				PostOptions = postOptions;
			}
			public string OptionsToString() {
				string s = (Options.Length > 0 ? Options[0] : "");
				for (int i = 1; i < Options.Length; i++)
					s += " " + Options[i];
				return s;
			}
		}

        #endregion
        //========== CONSTANTS ===========
        #region Constants

        /**<summary>The collection of command line options.</summary>*/
        private static readonly Dictionary<ArgTypes, OptionInfo> Options = new Dictionary<ArgTypes, OptionInfo>() {
    { ArgTypes.Input,    new OptionInfo(ProcessInput, "输入", "指定输入文件和文件夹。", "文件路径", "-i", "--input") },
    { ArgTypes.Output,   new OptionInfo(ProcessOutput, "输出", "指定输出文件和文件夹。", "文件路径", "-o", "--output") },
    #if !(CONSOLE)
    { ArgTypes.Console,  new OptionInfo(ProcessConsole, "控制台", "不显示进度窗口。", null, "-C", "--Console") },
    #endif
    { ArgTypes.Extract,  new OptionInfo(ProcessExtract, "提取", "设置为提取模式。", null, "-e", "--extract") },
    { ArgTypes.Convert,  new OptionInfo(ProcessConvert, "转换", "设置为转换模式。", null, "-c", "--convert") },
    { ArgTypes.Backup,   new OptionInfo(ProcessBackup, "备份", "设置为备份模式。", null, "-b", "--backup") },
    { ArgTypes.Restore,  new OptionInfo(ProcessRestore, "恢复", "设置为恢复模式。", null, "-r", "--restore") },
    { ArgTypes.Script,   new OptionInfo(ProcessScript, "脚本", "设置为运行脚本模式。", null, "-x", "--script") },

    { ArgTypes.Help,     new OptionInfo(ProcessHelp, "帮助", "显示帮助信息。", null, "-h", "--help") },
    { ArgTypes.Silent,   new OptionInfo(ProcessSilent, "静默", "不输出控制台信息。", null, "-s", "--silent") },
    
    #if !(CONSOLE)
    { ArgTypes.AutoClose,new OptionInfo(ProcessAutoClose, "自动关闭", "操作完成后自动关闭进度窗口。", null, "-a", "--auto-close") },
    { ArgTypes.KeepOpen,new OptionInfo(ProcessKeepOpen, "保持打开", "操作完成后保持进度窗口打开。", null, "-k", "--keep-open") },
    #endif
    
    { ArgTypes.Compress, new OptionInfo(ProcessCompress, "压缩", "图像将被压缩。", null, "-ic", "--compress") },
    { ArgTypes.DontCompress, new OptionInfo(ProcessDontCompress, "不压缩", "图像将不会被压缩。", null, "-dc", "--dont-compress") },
    { ArgTypes.Premultiply, new OptionInfo(ProcessPremultiply, "预乘", "RGB将按Alpha通道值进行预乘。", null, "-pr", "--premult") },
    { ArgTypes.DontPremultiply, new OptionInfo(ProcessDontPremultiply, "不预乘", "RGB将不被修改。", null, "-dp", "--dont-premult") },
};


        #endregion
        //=========== MEMBERS ============
        #region Members

        /**<summary>Arguments that have already been passed.</summary>*/
        private static ArgTypes passedArgs = ArgTypes.None;
		/**<summary>The last argument we're in. Used to wait for input.</summary>*/
		private static ArgTypes lastArg = ArgTypes.None;
		/**<summary>The current TConvert Mode. None equals any.</summary>*/
		private static ArgTypes argMode = ArgTypes.None;
		/**<summary>The current TConvert Mode. None equals any.</summary>*/
		private static ProcessModes processMode = ProcessModes.Any;
		/**<summary>The list of inputs filepaths.</summary>*/
		private static List<string> inputs = new List<string>();
		/**<summary>The list of output filepaths.</summary>*/
		private static List<string> outputs = new List<string>();
		/**<summary>True if an error has occurred.</summary>*/
		private static bool error = false;
		/**<summary>The file the log is being written to.</summary>*/
		private static string logFile = null;
		/**<summary>True if there is no console output.</summary>*/
		private static bool silent = false;
		/**<summary>True if the progress window auto-closes.</summary>*/
		private static bool autoClose = Settings.Default.AutoCloseCmdProgress;
		/**<summary>True if images are compressed.</summary>*/
		private static bool compress = Settings.Default.CompressImages && XCompress.IsAvailable;
		/**<summary>True if alpha is premultiplied.</summary>*/
		private static bool premultiply = Settings.Default.PremultiplyAlpha;
		/**<summary>True if a sound is played upon completion.</summary>*/
		private static bool sound = Settings.Default.CompletionSound;
		/**<summary>True if in console-only mode.</summary>*/
		private static bool console = false;
		/**<summary>The list of lines ready to be written to the console.</summary>*/
		private static List<KeyValuePair<ConsoleColor, string>> log = new List<KeyValuePair<ConsoleColor, string>>();
		/**<summary>Gets the name of the executable.</summary>*/
		private static string ExeName {
			get { return Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location); }
		}

		#endregion
		//=========== LOGGING ============
		#region Logging

		/**<summary>Logs a message with a color.</summary>*/
		private static void Log(ConsoleColor color, string message) {
			log.Add(new KeyValuePair<ConsoleColor, string>(
				color,
				message
			));
		}
		/**<summary>Logs a message.</summary>*/
		private static void Log(string message) {
			Log(ConsoleColor.Gray, message);
		}
        /**<summary>Logs an error message and sets error to true.</summary>*/
        private static void LogError(string message)
        {
            Log(ConsoleColor.Red, "错误: " + message); // 将 "Error: " 翻译为 "错误: "
            error = true;
        }

        /**<summary>记录警告信息。</summary>*/
        private static void LogWarning(string message)
        {
            Log(ConsoleColor.Yellow, "警告: " + message); // 将 "Warning: " 翻译为 "警告: "
        }

        /**<summary>记录模式已被指定的信息。</summary>*/
        private static void LogModeAlreadySpecified(ArgTypes argType)
        {
            LogError("TConvert 模式已指定为 " +
                Options[argMode].Name + " " + Options[argMode].OptionsToString() + ". " +
                "无法指定选项 " + Options[argType].Name + " (" +
                Options[argType].OptionsToString() + ")。"); // 将 "mode already specified" 和 "Cannot specify option" 翻译为中文
        }

        /**<summary>记录选项已被使用的信息。</summary>*/
        private static void LogOptionAlreadySpecified(ArgTypes argType)
        {
            LogError(Options[argMode].Name + " 选项 (" +
                Options[argMode].OptionsToString() + ") 已经被指定。"); // 将 "option already specified" 翻译为中文
        }

        /**<summary>Writes the log to the console.</summary>*/
        private static void WriteLog() {
			#if !(CONSOLE)
			Console.WriteLine();
			#endif
			ConsoleColor oldColor = Console.ForegroundColor;
			foreach (var line in log) {
				if (Console.ForegroundColor != line.Key)
					Console.ForegroundColor = line.Key;
				Console.WriteLine(line.Value);
			}
			Console.ForegroundColor = oldColor;
		}

		#endregion
		//=========== PARSING ============
		#region Parsing

		/**<summary>Parses the command line arguments. This is basically the console entry point.</summary>*/
		public static void ParseCommand(string[] args) {
            Log(ConsoleColor.White, "正在启动 TConvert..."); // 将 "Starting TConvert..." 翻译为 "正在启动 TConvert..."
            for (int i = 0; i < args.Length && !error; i++) {
				ArgTypes argType = ParseArgument(args[i]);
				passedArgs |= argType;
				if (argType >= ArgTypes.Extract && argType <= ArgTypes.Script) {
					argMode = argType;
				}
				if (argType != ArgTypes.Path || lastArg == ArgTypes.Log) {
					lastArg = argType;
				}
			}
			// Show a help message when there's no input
			if (args.Length == 0) {
				ProcessHelp();
			}
			if (!error) {
				ProcessCommand();
			}
			else if (!silent) {
				WriteLog();
			}
			Environment.ExitCode = (error ? 1 : 0);
		}
		/**<summary>Parses an argument.</summary>*/
		private static ArgTypes ParseArgument(string arg) {
			switch (lastArg) {
			case ArgTypes.Input:
				if (inputs.Count > 0)
					goto default;
				break;
			case ArgTypes.Output:
				if (outputs.Count > 0)
					goto default;
				break;
			case ArgTypes.Log:
				break;
			default:
				foreach (var argInfo in Options) {
					foreach (string option in argInfo.Value.Options) {
						if (arg == option) {
							argInfo.Value.Action();
							return argInfo.Key;
						}
					}
				}
				break;
			}
			ProcessFileName(arg);
			return ArgTypes.Path;
		}

		#endregion
		//========== PROCESSING ==========
		#region Processing
		//--------------------------------
		#region Final

		/**<summary>Final processing of the command after parsing.</summary>*/
		private static void ProcessCommand() {
            if (inputs.Count > 0 && outputs.Count == 0)
            {
                if (argMode == ArgTypes.Backup)
                {
                    LogError("未指定备份的输出文件。"); // "No outputs specified for backup."
                }
                else if (argMode == ArgTypes.Restore)
                {
                    LogError("未指定恢复的输出文件。"); // "No outputs specified for restore."
                }
                else
                {
                    foreach (string input in inputs)
                    {
                        outputs.Add(input);
                    }
                }
            }
            else if (inputs.Count != outputs.Count)
            {
                LogError("输入文件路径的数量必须与输出文件名的数量相同。"); // "Number of input filepaths must be the same number as output filenames."
            }

			for (int i = 0; i < inputs.Count && !error; i++)
			{
				string input = inputs[i];
				string output = outputs[i];
				string inputFull = "";
				string outputFull = "";
				try
				{
					inputFull = Path.GetFullPath(inputs[i]);
				}
				catch (ArgumentException)
				{
					LogError("无效输入路径: " + input);
					return;
				}

				try
				{
					outputFull = Path.GetFullPath(outputs[i]);
				}
				catch (ArgumentException)
				{
					LogError("无效输出路径: " + input);
					return;
				}

				if (string.Compare(inputFull, outputFull, true) == 0)
				{
					if (argMode == ArgTypes.Backup)
					{
						LogError("备份时输入路径与输出路径相同:\n    " + input + "\n    " + output);
						return;
					}
					else if (argMode == ArgTypes.Restore)
					{
						LogError("恢复时输入路径与输出路径相同:\n    " + input + "\n    " + output);
						return;
					}
				}

				if (File.Exists(input) || Directory.Exists(input))
				{
					bool isDir = Directory.Exists(input);
					bool outputExists = File.Exists(output) || Directory.Exists(output);
					if (outputExists && isDir != Directory.Exists(output))
					{
						if (isDir)
							LogError("输入是文件夹而输出是文件:\n    " + input + "\n    " + output);
						else
							LogError("输入是文件而输出是文件夹:\n    " + input + "\n    " + output);
						return;
					}
					inputs[i] = inputFull;
					outputs[i] = outputFull;
				}
				else
				{
					LogError("输入路径不存在: " + input);
				}
			}
                if (!silent)
				WriteLog();
			if (!error && inputs.Count > 0) {
				#if !(CONSOLE)
				if (!console) {
					App app = new App();
					app.InitializeComponent();
					app.Run();
				}
				else
				#endif
				{
					ProcessFiles();
				}
			}
		}
		/**<summary>Processes the files when ready.</summary>*/
		public static void ProcessFiles() {
			Thread thread = new Thread(() => {
				Processing.ProcessFiles(processMode, inputs.ToArray(), outputs.ToArray());
			});
			#if !(CONSOLE)
            if (!console)
            {
                Processing.StartProgressThread(null, "Processing Files...", autoClose, compress, sound, premultiply, thread);
            }
            else
			#endif
            {
                Processing.StartConsoleThread("Processing Files...", silent, compress, sound, premultiply, thread);
            }
        }

        #endregion
        //--------------------------------
        #region Input/Output

        ///<summary>处理输入选项。</summary>*/
        private static void ProcessInput()
        {
            if (passedArgs.HasFlag(ArgTypes.Input))
                LogOptionAlreadySpecified(ArgTypes.Input);
            else if (inputs.Count > 0)
                LogError("文件路径已指定。无法使用 " +
                    Options[ArgTypes.Input].Name + " 选项 (" +
                    Options[ArgTypes.Input].OptionsToString() + ")。");
        }
        /**<summary>处理输出选项。</summary>*/
        private static void ProcessOutput()
        {
            if (passedArgs.HasFlag(ArgTypes.Output))
                LogOptionAlreadySpecified(ArgTypes.Output);
        }
        /**<summary>处理文件名选项以及任何剩余的选项。</summary>*/
        private static void ProcessFileName(string arg)
        {
            if (arg.StartsWith("-") && !File.Exists(arg))
            {
                switch (lastArg)
                {
                    case ArgTypes.Input:
                        if (inputs.Count == 0)
                            LogError("预期为文件路径: " + arg);
                        else
                            goto default;
                        break;
                    case ArgTypes.Output:
                        if (outputs.Count == 0)
                            LogError("预期为文件路径: " + arg);
                        else
                            goto default;
                        break;
                    case ArgTypes.Log:
                        if (logFile == null)
                            LogError("预期为文件名: " + arg);
                        else
                            goto default;
                        break;
                    default:
                        LogError("无效选项: " + arg);
                        break;
                }
            }
            else
            {
                switch (lastArg)
                {
                    case ArgTypes.None:
                    case ArgTypes.Input:
                        inputs.Add(arg);
                        break;
                    case ArgTypes.Output:
                        outputs.Add(arg);
                        break;
                    case ArgTypes.Log:
                        logFile = arg;
                        break;
                    default:
                        LogError("无效选项: " + arg);
                        break;
                }
            }
        }

        #endregion

        //--------------------------------
        #region Modes

        /**<summary>Processes the Extract mode option.</summary>*/
        private static void ProcessExtract() {
			if (passedArgs.HasFlag(ArgTypes.Extract))
				LogOptionAlreadySpecified(ArgTypes.Extract);
			if (argMode != ArgTypes.None && argMode != ArgTypes.Extract)
				LogModeAlreadySpecified(ArgTypes.Extract);
			processMode = ProcessModes.Extract;
		}
		/**<summary>Processes the Convert mode option.</summary>*/
		private static void ProcessConvert() {
			if (passedArgs.HasFlag(ArgTypes.Convert))
				LogOptionAlreadySpecified(ArgTypes.Convert);
			if (argMode != ArgTypes.None && argMode != ArgTypes.Convert)
				LogModeAlreadySpecified(ArgTypes.Convert);
			processMode = ProcessModes.Convert;
		}
		/**<summary>Processes the Backup mode option.</summary>*/
		private static void ProcessBackup() {
			if (passedArgs.HasFlag(ArgTypes.Backup))
				LogOptionAlreadySpecified(ArgTypes.Backup);
			if (argMode != ArgTypes.None && argMode != ArgTypes.Backup)
				LogModeAlreadySpecified(ArgTypes.Backup);
			processMode = ProcessModes.Convert;
		}
		/**<summary>Processes the Restore mode option.</summary>*/
		private static void ProcessRestore() {
			if (passedArgs.HasFlag(ArgTypes.Restore))
				LogOptionAlreadySpecified(ArgTypes.Restore);
			if (argMode != ArgTypes.None && argMode != ArgTypes.Restore)
				LogModeAlreadySpecified(ArgTypes.Restore);
			processMode = ProcessModes.Restore;
		}
		/**<summary>Processes the Script mode option.</summary>*/
		private static void ProcessScript() {
			if (passedArgs.HasFlag(ArgTypes.Script))
				LogOptionAlreadySpecified(ArgTypes.Script);
			if (argMode != ArgTypes.None && argMode != ArgTypes.Script)
				LogModeAlreadySpecified(ArgTypes.Script);
			processMode = ProcessModes.Script;
		}

        #endregion
        //--------------------------------
        #region Misc

        /**<summary>Processes the Help option.</summary>*/
        private static void ProcessHelp()
        {
            Log(ConsoleColor.White, "[" + ExeName + "] 一个用于管理Terraria内容资源的组合工具。");
            Log("用法: " + ExeName + " [文件路径] [选项]");
            Log("  选项:");
            foreach (var argInfo in Options)
            {
                string line = "    ";
                line += argInfo.Value.OptionsToString();
                if (argInfo.Value.PostOptions != null)
                    line += " " + argInfo.Value.PostOptions;
                if (line.Length < 22)
                    line += new string(' ', 22 - line.Length);
                else if (line.Length < 27)
                    line += new string(' ', 27 - line.Length);
                line += argInfo.Value.Description;
                Log(line);
            }
        }

        /**<summary>Processes the Log option.</summary>*/
        private static void ProcessLog() {
			if (logFile != null)
				LogOptionAlreadySpecified(ArgTypes.Log);
		}
		/**<summary>Processes the Silent option.</summary>*/
		private static void ProcessSilent() {
			if (silent)
				LogOptionAlreadySpecified(ArgTypes.Silent);
			else
				silent = true;
		}
		/**<summary>Processes the AutoClose option.</summary>*/
		private static void ProcessAutoClose() {
			if (passedArgs.HasFlag(ArgTypes.AutoClose))
				LogOptionAlreadySpecified(ArgTypes.AutoClose);
			if (passedArgs.HasFlag(ArgTypes.KeepOpen))
				LogOptionAlreadySpecified(ArgTypes.KeepOpen);
			else
				autoClose = true;
		}
		/**<summary>Processes the KeepOpen option.</summary>*/
		private static void ProcessKeepOpen() {
			if (passedArgs.HasFlag(ArgTypes.AutoClose))
				LogOptionAlreadySpecified(ArgTypes.AutoClose);
			if (passedArgs.HasFlag(ArgTypes.KeepOpen))
				LogOptionAlreadySpecified(ArgTypes.KeepOpen);
			else
				autoClose = false;
		}
		/**<summary>Processes the AutoCloseDefault option.</summary>*/
		/*private static void ProcessAutoCloseDefault() {
			if (passedArgs.HasFlag(ArgTypes.AutoCloseDef))
				LogOptionAlreadySpecified(ArgTypes.AutoCloseDef);
			if (passedArgs.HasFlag(ArgTypes.KeepOpenDef))
				LogOptionAlreadySpecified(ArgTypes.KeepOpenDef);
			else {
				Settings.Default.AutoCloseCmdProgress = true;
				Settings.Default.Save();
				Log(ConsoleColor.White, "Progress window finish set to: Auto-Close");
			}
		}*/
		/**<summary>Processes the KeepOpenDefault option.</summary>*/
		/*private static void ProcessKeepOpenDefault() {
			if (passedArgs.HasFlag(ArgTypes.AutoCloseDef))
				LogOptionAlreadySpecified(ArgTypes.AutoCloseDef);
			if (passedArgs.HasFlag(ArgTypes.KeepOpenDef))
				LogOptionAlreadySpecified(ArgTypes.KeepOpenDef);
			else {
				Settings.Default.AutoCloseCmdProgress = false;
				Settings.Default.Save();
				Log(ConsoleColor.White, "Progress window finish set to: Keep Open");
			}
		}*/
		/**<summary>Processes the Console option.</summary>*/
		private static void ProcessConsole() {
			if (console)
				LogOptionAlreadySpecified(ArgTypes.Console);
			else
				console = true;
		}
		/**<summary>Processes the Compress option.</summary>*/
		private static void ProcessCompress() {
			if (passedArgs.HasFlag(ArgTypes.Compress))
				LogOptionAlreadySpecified(ArgTypes.Compress);
			if (passedArgs.HasFlag(ArgTypes.DontCompress))
				LogOptionAlreadySpecified(ArgTypes.DontCompress);
			else
				compress = true;
		}
		/**<summary>Processes the Compress option.</summary>*/
		private static void ProcessDontCompress() {
			if (passedArgs.HasFlag(ArgTypes.Compress))
				LogOptionAlreadySpecified(ArgTypes.Compress);
			if (passedArgs.HasFlag(ArgTypes.DontCompress))
				LogOptionAlreadySpecified(ArgTypes.DontCompress);
			else
				compress = false;
		}
		/**<summary>Processes the Premultiply option.</summary>*/
		private static void ProcessPremultiply() {
			if (passedArgs.HasFlag(ArgTypes.Premultiply))
				LogOptionAlreadySpecified(ArgTypes.Premultiply);
			if (passedArgs.HasFlag(ArgTypes.DontPremultiply))
				LogOptionAlreadySpecified(ArgTypes.DontPremultiply);
			else
				premultiply = true;
		}
		/**<summary>Processes the Premultiply option.</summary>*/
		private static void ProcessDontPremultiply() {
			if (passedArgs.HasFlag(ArgTypes.Premultiply))
				LogOptionAlreadySpecified(ArgTypes.Premultiply);
			if (passedArgs.HasFlag(ArgTypes.DontPremultiply))
				LogOptionAlreadySpecified(ArgTypes.DontPremultiply);
			else
				premultiply = false;
		}

		#endregion
		//--------------------------------
		#endregion
	}
}
