using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CSharp;
using ProjectPurifier.ViewModel;

namespace ProjectPurifier.Utils
{
	class Purifier
	{
		public const string SpecialPurificationCommandVisible = "__PURIFIER_INCLUDE_ALWAYS__";
		public const string SpecialPurificationCommandInvisible = "__PURIFIER_DELETE_ALWAYS__";

		static readonly Regex RegexDefineAssignment =  new Regex(@"#define (\w+)\s+(\w+)", RegexOptions.Compiled);
		static readonly Regex RegexFuncMacro =  new Regex(@"(\w+)\s*\((.*)\)\s+(.*)", RegexOptions.Compiled);
		static readonly Regex RegexDefined = new Regex(@"(.*?)defined\s*?\((.+?)\)\s*?(.*)", RegexOptions.Compiled);
		private Dictionary<string, DefineVM> _definesDict;
		private string _definitions_code;
		private string _helper_functions_code;

		public Purifier(IList<DefineVM> allDefines)
		{
			_definesDict = allDefines.ToDictionary(x => x.Name);
			var sbDefinitions = new StringBuilder();
			var sbFunctions = new StringBuilder();
			foreach (var defdata in allDefines)
			{
				// handle the case, the definition is an int
				if (int.TryParse(defdata.DefinedAs, NumberStyles.AllowParentheses | NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
				{
					sbDefinitions.AppendLine($"int {defdata.Name} = {intVal.ToString()};");
					continue; // success
				}
				else
				{
					Debug.WriteLine($"{defdata.Name}: '{defdata.DefinedAs}' cannot be parsed to int.");
				}

				// maybe it is a define to another define
				{
					var match = RegexDefineAssignment.Match(defdata.FullDefinition);
					if (match.Success)
					{
						sbDefinitions.AppendLine($"int {match.Groups[1].ToString()} = {match.Groups[2].ToString()};");
						continue; // success					
					}
					else
					{
						Debug.WriteLine($"{defdata.Name}: '{defdata.DefinedAs}' does not match define-assignment pattern.");
					}	
				}

				// maybe it is a macro-function?
				{		
					var match = RegexFuncMacro.Match(defdata.FullDefinition);
					if (match.Success)
					{
						var parametersString = match.Groups[2].ToString().Trim();
						var variablesStringBool = "";
						var variablesStringInt = "";
						if (parametersString.Length > 0)
						{
							var variables = parametersString.Split(',');
							foreach (var variable in variables)
							{
								variablesStringInt += $" int {variable}, ";
								variablesStringBool += $" bool {variable}, ";
							}
							variablesStringInt = variablesStringInt.Substring(0, variablesStringInt.Length - 2);
							variablesStringBool = variablesStringBool.Substring(0, variablesStringBool.Length - 2);

							if (variables.Length > 1)
								sbFunctions.AppendLine($"private static bool {match.Groups[1].ToString()} ({variablesStringInt}) {{ return {match.Groups[3].ToString()}; }}");
							else
								sbFunctions.AppendLine($"private static bool {match.Groups[1].ToString()} ({variablesStringBool}) {{ return {match.Groups[3].ToString()}; }}");
						}
						else
						{
							sbFunctions.AppendLine($"private static bool {match.Groups[1].ToString()} () {{ return {match.Groups[3].ToString()}; }}");
						}
						continue; // success
					}
					else
					{
						Debug.WriteLine($"{defdata.Name}: '{defdata.DefinedAs}' does not match macro-function pattern.");	
					}
				}
			}
			_definitions_code = sbDefinitions.ToString();
			_helper_functions_code = sbFunctions.ToString();
		}

		public CompilerResults CompileCode(string code)
		{
			CSharpCodeProvider provider = new CSharpCodeProvider();
			CompilerResults results = provider.CompileAssemblyFromSource(new CompilerParameters(), code);
			return results;
		}

		public MethodInfo GetMethodInAssembly(CompilerResults compilerResults, string type, string methodName)
		{
			Type binaryFunction = compilerResults.CompiledAssembly.GetType(type);
			var function = binaryFunction.GetMethod(methodName);
			return function;
		}
		

		public bool EvaluateBooleanExpression(string expression)
		{
			if (expression.Contains(Purifier.SpecialPurificationCommandInvisible))
			{
				return false;
			}
			if (expression.Contains(Purifier.SpecialPurificationCommandVisible))
			{
				return true;
			}

			// replace all defined() commands

			while(true)
			{
				var match = RegexDefined.Match(expression);
				if (match.Success)
				{
					expression = match.Groups[0].ToString()
					             + _definesDict.ContainsKey(match.Groups[1].ToString()).ToString()
					             + match.Groups[2].ToString();
				}
				else
				{
					break;
				}
			} 

			string[] codeVariants = new[]
			{
				@"using System;

				namespace PurifierInAction
				{{
					public class PurifierEvaluator
					{{
						{0}

						public static bool EvaluateBooleanExpression()
						{{
							{1}
							return {2};
						}}
					}}
				}}
				",
				@"using System;

				namespace PurifierInAction
				{{
					public class PurifierEvaluator
					{{
						{0}

						public static bool EvaluateBooleanExpression()
						{{
							{1}
							return 0 != ({2});
						}}
					}}
				}}
				",
				@"using System;

				namespace PurifierInAction
				{{
					public class PurifierEvaluator
					{{
						{0}

						public static bool EvaluateBooleanExpression()
						{{
							{1}
							return 0 != {2};
						}}
					}}
				}}
				",
				@"using System;

				namespace PurifierInAction
				{{
					public class PurifierEvaluator
					{{
						{0}

						public static bool EvaluateBooleanExpression()
						{{
							{1}
							return {2} != 0;
						}}
					}}
				}}
				",
			};
			
			MethodInfo methodInfo = null;
			foreach (var codeVariant in codeVariants)
			{
				try
				{
					var finalCode = string.Format(codeVariant, _helper_functions_code, _definitions_code, expression);
					var compiledCode = CompileCode(finalCode);
					methodInfo = GetMethodInAssembly(compiledCode, "PurifierInAction.PurifierEvaluator", "EvaluateBooleanExpression");
				}
				catch (Exception ex)
				{
					//Debug.WriteLine("Exception while trying bool-version: " + ex.Message);
				}

				if (null != methodInfo)
				{
					break;
				}
			}
			
			if (null == methodInfo)
			{
				throw new Exception($"Couldn't compile neither of the code versions. Please check your expression: {expression}");
			}
			
			var methodDlg = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), methodInfo);
			return methodDlg();
		}


	}
}
