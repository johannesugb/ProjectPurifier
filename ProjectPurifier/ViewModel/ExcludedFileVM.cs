using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProjectPurifier.ViewModel
{
	class ExcludedFileVM : BindableBase
	{
		private string _value;
		public string Value
		{
			get => _value;
			set
			{
				SetProperty(ref _value, value);
				RegexValue = _value.Replace(".", @"\.").Replace("*", @"[\w]*");
				TheRegex = new Regex(RegexValue, RegexOptions.Compiled);
			}
		}

		public string RegexValue { get; private set; }
		public Regex TheRegex { get; private set; }
	}
}
