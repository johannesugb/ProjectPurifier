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
		static readonly Regex RegexElse = new Regex(@"^\s*#else\s*", RegexOptions.Compiled);
		static readonly Regex RegexElif = new Regex(@"^\s*#elif\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexEndif = new Regex(@"^\s*#endif\s*", RegexOptions.Compiled);
		static readonly Regex RegexIfdef = new Regex(@"^\s*#ifdef\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexIfndef = new Regex(@"^\s*#ifndef\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexInclude = new Regex(@"^\s*#include\s+[\<\""]([\w.]+)[\>\""]", RegexOptions.Compiled);
		static readonly Regex RegexClInclude = new Regex(@".*\<ClInclude.*Include\s*=\s*\""([\w.\\\/]+)\""\s*?.*?(\/\>|\>)", RegexOptions.Compiled);
		static readonly Regex NoneInclude = new Regex(@".*\<None.*Include\s*=\s*\""([\w.\\\/]+)\""\s*?.*?(\/\>|\>)", RegexOptions.Compiled);
		static readonly Regex RegexClCompile = new Regex(@".*\<ClCompile.*Include\s*=\s*\""([\w.]+)\""\s*?.*?(\/\>|\>)", RegexOptions.Compiled);
		static readonly Regex RegexFilter = new Regex(@".*<Filter.*Include\s*=\s*\""([\w.]+)\""\s*?.*?(\/\>|\>)", RegexOptions.Compiled);

		private string _inputFolders;
		private string _purifierConfigFile;
		private string _outputFolder;
	    private string _inspectionFile;
	    private string _processedFilecontents;
	    private bool _currentlyPurifying;
	    public ObservableCollection<ExcludedFileVM> ExcludedFiles { get; } = new ObservableCollection<ExcludedFileVM>();
	    public ObservableCollection<ExcludedFilterVM> ExcludedFilters { get; } = new ObservableCollection<ExcludedFilterVM>();
	    public ObservableCollection<DefineVM> Defines { get; } = new ObservableCollection<DefineVM>();
	    private Dictionary<string, DefineVM> _definesDict;
	    public ObservableCollection<string> Errors { get; } = new ObservableCollection<string>();

	    public Purifier Purifier { get; set; }

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
				try
				{
					Purifier = new Purifier(Defines);
					var sb = GetPurified(File.ReadAllLines(_inspectionFile));
					ProcessedFilecontents = sb.ToString();
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			}
		}

	    public bool CurrentlyPurifying
	    {
		    get => _currentlyPurifying;
			set => SetProperty(ref _currentlyPurifying, value);
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
				var inputItems = InputFolders.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);
				var outputItems = OutputFolder.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);
				// ###### 0. Check if input and output could match ######
				if (inputItems.Length != outputItems.Length)
				{
					MessageBox.Show("There must be as many output-items as there are output-items! Aborting...");
					return;
				}

				// ####### 1. See if the files/folders exist and delete them #######
				string existingItems = string.Empty;
				foreach (var outputItem in outputItems)
				{
					if (Directory.Exists(outputItem))
					{
						existingItems += new DirectoryInfo(outputItem).Name + Environment.NewLine;
					}
					if (File.Exists(outputItem))
					{
						existingItems += new FileInfo(outputItem).Name + Environment.NewLine;
					}
				}
				if (!string.IsNullOrEmpty(existingItems))
				{
					var mbres = MessageBox.Show("The following output files/directories already exist:" + Environment.NewLine +
											    existingItems +
												"If we proceed, they will be deleted!", "Delete existing items", MessageBoxButton.OKCancel);
					if (mbres == MessageBoxResult.OK)
					{
						foreach (var outputItem in outputItems)
						{
							if (Directory.Exists(outputItem))
							{
								Directory.Delete(outputItem, true);
							}
							if (File.Exists(outputItem))
							{
								File.Delete(outputItem);
							}
						}
					}
					else
					{
						return;
					}
				}

				CurrentlyPurifying = true;
				Dispatcher.CurrentDispatcher.Invoke(delegate
				{
					try
					{
						// ####### 2. Copy all the files and folders #######
						for (int i=0; i < inputItems.Length; ++i)
						{
							var inputItem = inputItems[i];
							var outputItem = outputItems[i];

							if (Directory.Exists(inputItem))
							{
								Directory.CreateDirectory(outputItem);

								//Copy all the files & Replaces any files with the same name
								foreach (string filepath in Directory.GetFiles(inputItem, "*.*", SearchOption.AllDirectories))
								{
									// check if it is an excluded file
									if (IsFileToBeExcluded(filepath))
									{
										continue;
									}

									var destination = filepath.Replace(inputItem, outputItem);
									
									var fileInfoOut = new FileInfo(destination);
									Directory.CreateDirectory(fileInfoOut.Directory.FullName);

									File.Copy(filepath, destination, true);

									// ####### 3. Purify each and every file (maybe) #######
									if (new FileInfo(destination).Length > 524288L) // only check if bigger than 512KiB
									{	
										if (FileTypeDetector.MightBeBinary(destination))
										{
											Console.WriteLine($"Skipping file '{destination}' because it might be binary.");
											continue;
										}
										if (!FileTypeDetector.MightBeText(out var tmp, destination))
										{
											Console.WriteLine($"Skipping file '{destination}' because it might not be text.");
											continue;
										}
									}
									if (!PurifyFile(destination))
										throw new Exception("PurifyFile failed");
								}
							}
							else if (File.Exists(inputItem))
							{
								if (!IsFileToBeExcluded(inputItem))
								{
									// Create destination directory if it not already exists:
									var fileInfoOut = new FileInfo(outputItem);
									Directory.CreateDirectory(fileInfoOut.Directory.FullName);
									
									File.Copy(inputItem, outputItem, true);

									// ####### 3. Purify each and every file (maybe) #######
									if (new FileInfo(outputItem).Length > 524288L) // only check if bigger than 512KiB
									{
										if (FileTypeDetector.MightBeBinary(outputItem))
										{
											Console.WriteLine($"Skipping file '{outputItem}' because it might be binary.");
											continue;
										}
										if (!FileTypeDetector.MightBeText(out var tmp, outputItem))
										{
											Console.WriteLine($"Skipping file '{outputItem}' because it might not be text.");
											continue;
										}
									}
									if (!PurifyFile(outputItem))
										throw new Exception("PurifyFile failed");
								}
							}							
						}
						
						CurrentlyPurifying = false;
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex);
						CurrentlyPurifying = false;
					}
				}, DispatcherPriority.ApplicationIdle);
			}, _ => !string.IsNullOrWhiteSpace(OutputFolder));

			// initially, set the values from the config
		    InputFolders = Settings.Default.InputFoldersLastValue;
		    PurifierConfigFile = Settings.Default.PurifierConfigFileLastValue;
		    OutputFolder = Settings.Default.OutputFolderLastValue;
		    InspectionFile = Settings.Default.FileToInspectLastValue;
	    }

	    private bool IsOutputFolderEmpty()
	    {
		    try
		    {
				return string.IsNullOrWhiteSpace(OutputFolder) || !Directory.EnumerateFileSystemEntries(OutputFolder).Any();
		    }
		    catch (Exception ex)
		    {
			    Debug.WriteLine(ex);
			    return false;
		    }
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
		    if (macroDefinition.Contains(Purifier.SpecialPurificationCommandInvisible))
		    {
			    return true;
		    }
		    if (macroDefinition.Contains(Purifier.SpecialPurificationCommandVisible))
		    {
			    return true;
		    }

			foreach (var defineVm in Defines)
		    {
			    if (macroDefinition.Contains(defineVm.Name))
			    {
				    return true;
			    }
		    }
		    return false;
	    }

		// Returns true if the file is to be excluded
	    private bool IsFileToBeExcluded(string file)
	    {
		    foreach (var exclFile in ExcludedFiles)
		    {
			    if (exclFile.TheRegex.IsMatch(file))
				    return true;
		    }
		    return false;
	    }

	    // Returns true if the filter is to be excluded
	    private bool IsFilterToBeExcluded(string name)
	    {
		    foreach (var exclFltr in ExcludedFilters)
		    {
			    if (string.Equals(exclFltr.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase))
				    return true;
		    }
		    return false;
	    }

		/// <summary>
		/// Helper function to evaluate all kinds of ifs
		/// </summary>
		/// <param name="lineToMatch">contents of the line to be matched against</param>
		/// <param name="isPurifierExpression">whether or not the if was a purification-relevant one</param>
		/// <param name="contentIsVisible">evaluation result of the purifier-relevant expression</param>
		/// <param name="indent">Only used for debug purposes</param>
		/// <returns>true if an if could be matched</returns>
		bool DoesAnyIfMatch(string lineToMatch, out bool isPurifierExpression, out bool contentIsVisible, string indent = "")
	    {
		    // gather all if-regexes in one array, second parmeter means: negate expression result yes/no
		    Tuple<Regex, bool>[] ifs = {
			    new Tuple<Regex, bool>(RegexIf, false),
			    new Tuple<Regex, bool>(RegexIfdef, false),
			    new Tuple<Regex, bool>(RegexIfndef, true),
		    };

		    foreach (var tuple in ifs)
		    {
			    var match = tuple.Item1.Match(lineToMatch);
			    if (match.Success)
			    {
				    Debug.WriteLine($"{indent}{tuple.Item1.ToString()} matches");
				    var expr = match.Groups[1].ToString();
				    if (IsPurificationRelevantMacro(expr))
				    {
					    var exprResult = Purifier.EvaluateBooleanExpression(expr);
					    isPurifierExpression = true;
					    contentIsVisible = tuple.Item2 != exprResult;
					    return true;
				    }
				    else
				    {
					    isPurifierExpression = false;
					    contentIsVisible = true;
					    return true;
				    }
			    }
		    }
		    isPurifierExpression = false;
		    contentIsVisible = true;
		    return false;
	    }

		/// <summary>
		/// Helper function to evaluate elifs
		/// </summary>
		/// <param name="lineToMatch">contents of the line to be matched against</param>
		/// <param name="isPurifierExpression">whether or not the if was a purification-relevant one</param>
		/// <param name="contentIsVisible">evaluation result of the purifier-relevant expression</param>
		/// <param name="indent">Only used for debug purposes</param>
		/// <returns>true if an elif could be matched</returns>
		bool DoesElifMatch(string lineToMatch, out bool isPurifierExpression, out bool contentIsVisible, string indent = "")
	    {
			var match = RegexElif.Match(lineToMatch);
			if (match.Success)
			{
				Debug.WriteLine($"{indent}#elif matches");
				var expr = match.Groups[1].ToString();
				if (IsPurificationRelevantMacro(expr))
				{
					var exprResult = Purifier.EvaluateBooleanExpression(expr);
					isPurifierExpression = true;
					contentIsVisible = exprResult;
					return true;
				}
				else
				{
					isPurifierExpression = false;
					contentIsVisible = true;
					return true;
				}
			}
		    isPurifierExpression = false;
		    contentIsVisible = true;
		    return false;
		}

		/// <summary>
		/// Handles block of code or a sub-block of code
		/// </summary>
		/// <param name="lines">all input lines</param>
		/// <param name="lineIndex">current line index</param>
		/// <param name="isVisible">whether or not the current region's contents are to be included in the output or not</param>
		/// <param name="sb">The StringBuilder which builds the output file</param>
		/// <param name="indent">Only used for debug purposes</param>
		/// <returns>last line index which has been processed (and possible also added to the StringBuilder)</returns>
		private int HandleRegion(string[] lines, int lineIndex, bool isVisible, StringBuilder sb, string indent = "")
	    {
			#if DEBUG
		    if (lineIndex > 0)
		    {
			    Debug.WriteLine($"{indent}Handle sub-region of: {lines[lineIndex-1]}");
		    }
			#endif
			
			while (lineIndex < lines.Length)
			{
				var curLine = lines[lineIndex];
				if (DoesAnyIfMatch(curLine, out bool isPurifierExpr, out bool contentIsVisi, indent))
				{
					if (!isPurifierExpr && isVisible)
					{
						sb.AppendLine(curLine);
					}
					lineIndex = HandleRegion(lines, lineIndex + 1, isVisible && (!isPurifierExpr || contentIsVisi), sb, indent + "    ");
					curLine = lines[lineIndex];
					var elseVisible = isVisible && (!isPurifierExpr || !contentIsVisi);
					while (DoesElifMatch(curLine, out bool elifIsPurifierExpr, out bool elifContentIsVisi, indent))
					{
						if (!isPurifierExpr && elseVisible)
						{
							sb.AppendLine(curLine);
						}
						lineIndex = HandleRegion(lines, lineIndex + 1, isVisible && elseVisible && (!isPurifierExpr || elifContentIsVisi), sb, indent + "    ");
						curLine = lines[lineIndex];
						elseVisible = isVisible && (!isPurifierExpr || !elifContentIsVisi);
					}
					if (RegexElse.IsMatch(curLine))
					{
						if (!isPurifierExpr && elseVisible)
						{
							sb.AppendLine(curLine);
						}
						lineIndex = HandleRegion(lines, lineIndex + 1, isVisible && elseVisible, sb, indent + "    ");
						curLine = lines[lineIndex];
					}
					if (RegexEndif.IsMatch(curLine))
					{
						if (!isPurifierExpr && isVisible)
						{
							sb.AppendLine(curLine);
						}
						lineIndex += 1;
						continue;
					}
				}

				if (RegexElif.IsMatch(curLine) || RegexElse.IsMatch(curLine) || RegexEndif.IsMatch(curLine))
				{
					return lineIndex;
				}
				
				// Bounds are checked by the while-loop, just add the line if it is visible
				if (isVisible)
				{
					{
						var match = RegexInclude.Match(curLine);
						if (match.Success)
						{
							if (IsFileToBeExcluded(match.Groups[1].ToString()))
							{
								lineIndex += 1;
								continue;
							}
						}
					}

					{
						var match = RegexClInclude.Match(curLine);
						if (match.Success)
						{
							if (IsFileToBeExcluded(match.Groups[1].ToString()))
							{
								if (match.Groups[2].ToString() == "/>")
								{
									lineIndex += 1;
									continue;
								}
								else
								{
									Debug.Assert(match.Groups[2].ToString() == ">");
									while (!curLine.Contains(@"</ClInclude>"))
									{
										lineIndex += 1;
										curLine = lines[lineIndex];
									}
									lineIndex += 1;
								}
								continue;
							}
						}
					}

					{
						var match = NoneInclude.Match(curLine);
						if (match.Success)
						{
							if (IsFileToBeExcluded(match.Groups[1].ToString()))
							{
								if (match.Groups[2].ToString() == "/>")
								{
									lineIndex += 1;
									continue;
								}
								else
								{
									Debug.Assert(match.Groups[2].ToString() == ">");
									while (!curLine.Contains(@"</None>"))
									{
										lineIndex += 1;
										curLine = lines[lineIndex];
									}
									lineIndex += 1;
								}
								continue;
							}
						}
					}

					{
						var match = RegexClCompile.Match(curLine);
						if (match.Success)
						{
							if (IsFileToBeExcluded(match.Groups[1].ToString()))
							{
								if (match.Groups[2].ToString() == "/>")
								{
									lineIndex += 1;
									continue;
								}
								else
								{
									Debug.Assert(match.Groups[2].ToString() == ">");	
									while (!curLine.Contains(@"</ClCompile>"))
									{
										lineIndex += 1;
										curLine = lines[lineIndex];
									}
									lineIndex += 1;
								}
								continue;
							}
						}
					}

					{
						var match = RegexFilter.Match(curLine);
						if (match.Success)
						{
							if (IsFilterToBeExcluded(match.Groups[1].ToString()))
							{
								if (match.Groups[2].ToString() == "/>")
								{
									lineIndex += 1;
									continue;
								}
								else
								{
									Debug.Assert(match.Groups[2].ToString() == ">");	
									while (!curLine.Contains(@"</Filter>"))
									{
										lineIndex += 1;
										curLine = lines[lineIndex];
									}
									lineIndex += 1;
								}
								continue;
							}
						}
					}

					sb.AppendLine(curLine);
				}
				lineIndex += 1;
			}
		    return lines.Length;
	    }

		/// <summary>
		/// Walk through the given file, line by line, and purify it!
		/// </summary>
		/// <param name="filepath">Path to the file to be purified</param>
	    public bool PurifyFile(string filepath)
		{
			if (string.IsNullOrWhiteSpace(filepath) || !File.Exists(filepath))
			{
				return false;
			}

			try
			{
				Purifier = new Purifier(Defines);
				var inputLines = File.ReadAllLines(filepath);
				var sb = GetPurified(inputLines);
				using (var sw = new StreamWriter(filepath))
				{
					sw.WriteLine(sb.ToString()); 
				}

				return true;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Walk through the given file, line by line, and purify it!
		/// </summary>
		/// <param name="inputLines">Source of the file, line by line</param>
		public StringBuilder GetPurified(string[] inputLines)
	    {
			var sb = new StringBuilder();
		    var idx = HandleRegion(inputLines, 0, true, sb);
		    Debug.Assert(idx == inputLines.Length);
		    return sb;
	    }
		
	}
}
