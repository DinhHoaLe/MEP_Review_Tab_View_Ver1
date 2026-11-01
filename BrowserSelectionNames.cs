using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class BrowserSelectionNames
    {
        internal static List<string> Read(IEnumerable items,IEnumerable indices)
        {
            var selected=new HashSet<int>();
            foreach(var value in indices)selected.Add(Convert.ToInt32(value));
            var names=new List<string>();
            foreach(var item in items)
            {
                try
                {
                    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                    var type=item.GetType();
                    var index=type.GetMethod("GetIndex",flags);
                    if(index!=null && selected.Contains(Convert.ToInt32(index.Invoke(item,null))))
                    {
                        string name=type.GetMethod("GetName",flags)?.Invoke(item,null) as string;
                        if(!string.IsNullOrWhiteSpace(name))names.Add(name);
                    }
                }
                finally { (item as IDisposable)?.Dispose(); }
            }
            return names;
        }
    }
}
