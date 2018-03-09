using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CSharp;
using ProjectPurifier.ViewModel;

namespace ProjectPurifier.Utils
{
	class Purification
	{
		private Dictionary<string, DefineVM> _definesDict;
		private string _definitions_code;

		public Purification(IList<DefineVM> allDefines)
		{
			_definesDict = allDefines.ToDictionary(x => x.Name);
			var sb = new StringBuilder();
			foreach (var defdata in allDefines)
			{
				// handle the case, the definition is an int (which is the only case right now)
				if (!int.TryParse(defdata.DefinedAs, NumberStyles.AllowParentheses | NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
				{
					Debug.WriteLine($"Skipping {defdata.Name} because couldn't parse '{defdata.DefinedAs}' to int.");
					continue;
				}
				sb.Append("int ");
				sb.Append(defdata.Name);
				sb.Append(" = ");
				sb.Append(intVal.ToString());
				sb.AppendLine(";");
			}
			_definitions_code = sb.ToString();
		}

		public bool EvaluateBooleanExpression(string expression)
		{
			string code = @"
			using System;
            
			namespace PurifierInAction
			{                
				public class PurifierEvaluator
				{                
					public static bool EvaluateBooleanExpression()
					{
						ABCDEFGHIJK
						return LMNOPQRSTUVWXYZ;
					}
				}
			}
			";

			string finalCode = code.Replace("ABCDEFGHIJK", _definitions_code).Replace("LMNOPQRSTUVWXYZ", expression);

			CSharpCodeProvider provider = new CSharpCodeProvider();
			CompilerResults results = provider.CompileAssemblyFromSource(new CompilerParameters(), finalCode);

			Type binaryFunction = results.CompiledAssembly.GetType("PurifierInAction.PurifierEvaluator");
			var function = binaryFunction.GetMethod("EvaluateBooleanExpression");

			var del = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), function);
			return del();
		}


	}
}
