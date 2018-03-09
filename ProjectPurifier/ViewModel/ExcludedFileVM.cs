using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectPurifier.ViewModel
{
	class ExcludedFileVM : BindableBase
	{
		private string _value;
		public string Value
		{
			get => _value;
			set => SetProperty(ref _value, value);
		}
	}
}
