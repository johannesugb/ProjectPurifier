using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectPurifier.ViewModel
{
	class DefineVM : BindableBase
	{
		private string _name;
		public string Name
		{
			get => _name;
			set => SetProperty(ref _name, value);
		}

		private string _definedAs;
		public string DefinedAs
		{
			get => _definedAs;
			set => SetProperty(ref _definedAs, value);
		}
	}
}
