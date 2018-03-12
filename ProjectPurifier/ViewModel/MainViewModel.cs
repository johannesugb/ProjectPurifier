using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ProjectPurifier.Commands;
using ProjectPurifier.Properties;
using ProjectPurifier.Utils;

namespace ProjectPurifier.ViewModel
{
    class MainViewModel : BindableBase
    {
		static readonly Regex RegexExcludeFiles =  new Regex(@"^\s*#pragma\s+purifier_exclude_files\s*\(([\w.\*]+)\s*\)", RegexOptions.Compiled);
		static readonly Regex RegexExcludeFilter = new Regex(@"^\s*#pragma\s+purifier_exclude_filter\s*\(([\w.\*]+)\s*\)", RegexOptions.Compiled);
	    static readonly Regex RegexDefine = new Regex(@"^\s*#define\s+([\w]+)\s+(.*)", RegexOptions.Compiled);
	    static readonly Regex RegexFuncMacro =  new Regex(@"^\s*#define\s+(\w+)\s*\((.*)\)\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexIf = new Regex(@"^\s*#if\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexElse = new Regex(@"^\s*#else\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexElif = new Regex(@"^\s*#elif\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexEndif = new Regex(@"^\s*#endif\s*", RegexOptions.Compiled);
		static readonly Regex RegexIfdef = new Regex(@"^\s*#ifdef\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexIfndef = new Regex(@"^\s*#ifndef\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexInclude = new Regex(@"^\s+#include\s+[\<\""]([\w.] +)[\>\""]", RegexOptions.Compiled);

		private string _inputFolders;
		private string _purifierConfigFile;
		private string _outputFolder;
	    private string _inspectionFile;
	    private string _processedFilecontents;
	    public ObservableCollection<ExcludedFileVM> ExcludedFiles { get; } = new ObservableCollection<ExcludedFileVM>();
	    public ObservableCollection<ExcludedFilterVM> ExcludedFilters { get; } = new ObservableCollection<ExcludedFilterVM>();
	    public ObservableCollection<DefineVM> Defines { get; } = new ObservableCollection<DefineVM>();
	    private Dictionary<string, DefineVM> _definesDict;
	    public ObservableCollection<string> Errors { get; } = new ObservableCollection<string>();
    
	    public ICommand RefreshTextbox { get; }
	    public ICommand JustDoIt { get; }
		
		public string InputFolders
	    {
		    get => _inputFolders;
			set
			{
				SetProperty(ref _inputFolders, value);
				Settings.Default.InputFoldersLastValue = value;
				Settings.Default.Save();
			}
	    }

	    public string PurifierConfigFile
		{
		    get => _purifierConfigFile;
		    set
		    {
			    SetProperty(ref _purifierConfigFile, value);	
			    Settings.Default.PurifierConfigFileLastValue = value; 
				Settings.Default.Save();
				FireAllCanExecuteChanged();
			    LoadDefinesAndStuff();
		    }
	    }

	    public string OutputFolder
		{
		    get => _outputFolder;
			set
			{
				SetProperty(ref _outputFolder, value);
				Settings.Default.OutputFolderLastValue = value;
				Settings.Default.Save();
				FireAllCanExecuteChanged();
			}
		}

	    public string InspectionFile
	    {
		    get => _inspectionFile;
			set
			{
				SetProperty(ref _inspectionFile, value);
				Settings.Default.FileToInspectLastValue = value;
				Settings.Default.Save();
				FireAllCanExecuteChanged();
				PurifyFile(_inspectionFile);
			}
		}

	    public string ProcessedFilecontents
		{
		    get => _processedFilecontents;
			set
			{
				SetProperty(ref _processedFilecontents, value);
				FireAllCanExecuteChanged();
			}
		}

	    public MainViewModel()
	    {
		    RefreshTextbox = new DelegateCommand(x =>
		    {
			    var tb = x as TextBox;
			    Debug.Assert(null != tb);
			    BindingOperations.GetBindingExpressionBase(tb, TextBox.TextProperty).UpdateSource();
		    }, x => null != x);

			JustDoIt = new DelegateCommand(_ =>
			{
				// TODO: Parse!!!
			}, _ => IsOutputFolderEmpty() && DoesOutputFolderExist());

			// initially, set the values from the config
		    InputFolders = Settings.Default.InputFoldersLastValue;
		    PurifierConfigFile = Settings.Default.PurifierConfigFileLastValue;
		    OutputFolder = Settings.Default.OutputFolderLastValue;
		    InspectionFile = Settings.Default.FileToInspectLastValue;
	    }

	    private bool IsOutputFolderEmpty()
	    {
			return string.IsNullOrWhiteSpace(OutputFolder) || !Directory.EnumerateFileSystemEntries(OutputFolder).Any();
		}

	    private bool DoesOutputFolderExist()
	    {
		    return Directory.Exists(OutputFolder);
	    }

	    private void LoadDefinesAndStuff()
	    {
			if (string.IsNullOrWhiteSpace(PurifierConfigFile) || !File.Exists(PurifierConfigFile))
			{
				return;
			}

		    try
		    {

			    var defLines = File.ReadAllLines(PurifierConfigFile);
			    
				Defines.Clear();
				ExcludedFiles.Clear();
				ExcludedFilters.Clear();
			    Errors.Clear();

				foreach (var line in defLines)
			    {
					// check if it is an excluded file
				    {
						var match = RegexExcludeFiles.Match(line);
						if (match.Success)
						{
							ExcludedFiles.Add(new ExcludedFileVM
							{
								Value = match.Groups[1].ToString()
							});
						}
				    }

					// check if it is an excluded filter
				    {
						var match = RegexExcludeFilter.Match(line);
						if (match.Success)
						{
							ExcludedFilters.Add(new ExcludedFilterVM
							{
								Name = match.Groups[1].ToString()
							});
						}
				    }

					// check if it is a define
				    {
					    var match = RegexDefine.Match(line);
					    if (match.Success)
					    {
						    var defineName = match.Groups[1].ToString();
						    var definedAs = match.Groups[2].ToString();

							// do we already have this define?
						    var found = Defines.FirstOrDefault(x => x.Name == defineName);
						    var error = false;
						    if (null != found)
						    {
								// check if it has the same definition as the previous one
							    if (found.DefinedAs != definedAs)
							    {
								    Errors.Add($"Multiple definitions found for {defineName} which differ.");
							    }
							    error = true;
						    }

						    if (!error)
						    {
								Defines.Add(new DefineVM
								{
									FullDefinition = line,
									Name = defineName,
									DefinedAs = definedAs
								});
						    }
					    }
				    }

				    // check if it is a function-macro define
				    {
					    var match = RegexFuncMacro.Match(line);
					    if (match.Success)
					    {
						    var defineName = match.Groups[1].ToString();
						    var definedAs = line.Substring(line.IndexOf("#define", StringComparison.InvariantCulture) + "#define".Length);

						    // do we already have this define?
						    var found = Defines.FirstOrDefault(x => x.Name == defineName);
						    var error = false;
						    if (null != found)
						    {
							    // check if it has the same definition as the previous one
							    if (found.DefinedAs != definedAs)
							    {
								    Errors.Add($"Multiple definitions found for {defineName} which differ.");
							    }
							    error = true;
						    }

						    if (!error)
						    {
							    Defines.Add(new DefineVM
							    {
								    FullDefinition = line,
								    Name = defineName,
								    DefinedAs = definedAs
							    });
						    }
					    }
				    }
				}

		    }
		    catch (Exception ex)
		    {
			    MessageBox.Show(ex.Message);
		    }
	    }

		/// <summary>
		/// It is purification-relevant, if it contains any of the #defines from the Purifier-Config-File
		/// </summary>
		/// <param name="macroDefinition">The macro definition, i.e. everything after the "#if"</param>
		/// <returns>true if one of the Defines is contained in the macroDefinition</returns>
		private bool IsPurificationRelevantMacro(string macroDefinition)
	    {
		    foreach (var defineVm in Defines)
		    {
			    if (macroDefinition.Contains(defineVm.Name))
			    {
				    return true;
			    }
		    }
		    return false;
	    }

		/// <summary>
		/// Handles an opened #if (or similar) which does not contain purification-relevant variables.
		/// Returns the next index to proceed after it has completed its work.
		/// </summary>
		/// <param name="lines">all input lines</param>
		/// <param name="lineIndex">current line index</param>
		/// <param name="isVisible">whether or not this current #if's contents are to be included in the output or not</param>
		/// <param name="sb">The StringBuilder which builds the output file</param>
		/// <returns></returns>
		private int HandlePurificationIrrelevantOpenedControlFlow(string[] lines, int lineIndex, bool isVisible, StringBuilder sb)
	    {
		    return -1;
	    }

		/// <summary>
		/// Handles an opened #if (or similar) which DOES contain purification-relevant variables.
		/// Returns the next index to proceed after it has completed its work.
		/// </summary>
		/// <param name="lines">all input lines</param>
		/// <param name="lineIndex">current line index</param>
		/// <param name="isVisible">whether or not this current #if's contents are to be included in the output or not</param>
		/// <param name="sb">The StringBuilder which builds the output file</param>
		/// <returns></returns>
		private int HandleOpenedControlFlow(string[] lines, int lineIndex, bool isVisible, StringBuilder sb)
	    {
			for (int i = 0; i < lines.Length; ++i)
			{
				var curLine = lines[i];
			    // search for either of the followint: #else, #elif, #endif
				{
					var match = RegexElse.Match(curLine);
					if (match.Success)
					{
						return HandleOpenedControlFlow(lines, i + 1, !isVisible, sb);
					}
				}
			    
		    }
		    return -1;
	    }

		/// <summary>
		/// Walk through the given file, line by line, and purify it!
		/// </summary>
		/// <param name="filepath">Path to the file to be purified</param>
	    public void PurifyFile(string filepath)
		{
			if (string.IsNullOrWhiteSpace(filepath) || !File.Exists(filepath))
			{
				return;
			}

			try
			{
				var purifier = new Purifier(Defines);
				var inputLines = File.ReadAllLines(InspectionFile);

				var sb = new StringBuilder();
				int idx = 0;
				while (idx < inputLines.Length)
				{
					var inputLine = inputLines[idx];

					// Handle the "starting" cases of a block and one-liner-cases:

					{ // Handle #include, which are a one-liners
						var match = RegexInclude.Match(inputLine);
						if (match.Success)
						{
							bool exclude = false;
							foreach (var excludedFileVm in ExcludedFiles)
							{
								if (match.Groups[1].ToString().Contains(excludedFileVm.Value))
								{
									exclude = true;
									break;
								}
							}
							if (exclude)
							{
								continue;
							}
						}
					}

					{ // Handle #if
						var match = RegexIf.Match(inputLine);
						if (match.Success && IsPurificationRelevantMacro(match.Groups[1].ToString()))
						{
							if (purifier.EvaluateBooleanExpression(match.Groups[1].ToString()))
							{
								idx = HandleOpenedControlFlow(inputLines, idx, true, sb);
							}
							else
							{
								idx = HandleOpenedControlFlow(inputLines, idx, false, sb);
							}
						}
					}
					
					// no regex matched => treat it as a normal line
					sb.AppendLine(inputLine);
				}
				ProcessedFilecontents = sb.ToString();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}
	}
}
