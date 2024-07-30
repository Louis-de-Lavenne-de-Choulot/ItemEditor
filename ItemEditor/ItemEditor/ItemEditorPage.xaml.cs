using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CustomItemEditorAttributes;
using System.ComponentModel.DataAnnotations;
using System.Windows.Documents;

namespace ItemEditor
{
    public class VarMapper
    {
        public int ID { get; set; } = 0;
        private string? _name { get; set; }
        public string? VarName { get { return RequiredB ? (_name ?? "") + " *" : _name; } set { _name = value ?? ""; } }
        public object? VarValue { get; set; }
        public object? VarRealName { get; set; }
        public object? VarType { get; set; }
        public int Reference { get; set; }
        public string RemoveButton { get; set; }
        public ICollection<long> selectionIndexes { get; set; }
        public ICollection<string> SelectionOptions { get; set; }
        public bool Editable { get; set; } = false;
        public string? Required { get; set; }
        private bool _required { get; set; }
        public bool RequiredB
        {
            get { return _required; }
            set
            {
                Required = "#FF000000";
                _required = value;
            }
        }

        public string Last { get; set; } = "";
        public bool LastB
        {
            get { return Last != "" ? true : false; }
            set
            {
                Last = value ? "Y" : "";
            }
        }

        public VarMapper()
        {
            LastB = false;
            RequiredB = false;
            Reference = 0;
            RemoveButton = string.Empty;
            selectionIndexes = new List<long>();
            SelectionOptions = new List<string>();
        }
    }
    /// <summary>
    /// Logique d'interaction pour ItemEditorPage.xaml
    /// </summary>
    public partial class ItemEditorPage : Page
    {
        private readonly ObservableCollection<VarMapper> firstElmMapper = new ObservableCollection<VarMapper>();
        private Dictionary<string, Func<object, object?>> bindingFunctions = new Dictionary<string, Func<object, object?>>();
        private Dictionary<string, Dictionary<long, string>> nestedResult = new Dictionary<string, Dictionary<long, string>>();
        private object firstElm;
        private readonly Action? end;
        private string tempPropertyName = "";
        int countVarMappersPerInstance = 0;

        public ItemEditorPage(object firstElm, Dictionary<string, Func<object, object?>> bindingFunctions, string pageTitle = "", Action? end = null)
        {
            InitializeComponent();
            PageTitle.Text = pageTitle;
            this.bindingFunctions = bindingFunctions;
            this.firstElm = firstElm;
            this.end = end;

            object[] attrs = firstElm.GetType().GetCustomAttributes(true);
            Type tp = firstElm.GetType().IsGenericType ? firstElm.GetType().GetGenericTypeDefinition() : typeof(string);
            if (firstElm.GetType().IsGenericType && (tp == typeof(ICollection) || tp == typeof(IList) || tp == typeof(List<>)))
            {
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];
                attrs = innerType.GetCustomAttributes(true);
                PopulateMappersICollection();
            }
            else
                PopulateMappersClass();

            foreach (object attr in attrs)
            {
                CustomDescriptionAttribute? descriptionAttribute = attr as CustomDescriptionAttribute;
                if (descriptionAttribute != null)
                    PageTitle.Text = descriptionAttribute.Description;
            }
            BindItemsControl();
        }

        private void PopulateMappersICollection()
        {
            AddEntry.Tag = "Button";
            IList ICollectionObjs = (IList)firstElm;
            if (ICollectionObjs.Count == 0)
            {
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];

                // Create an instance of the inner object type
                try
                {
                    object? instance = Activator.CreateInstance(innerType);
                    ICollectionObjs.Add(instance ?? throw new Exception());
                    firstElm = ICollectionObjs;

                }
                catch
                {
                }
            }
            int count = 0;
            foreach (object? obj in ICollectionObjs)
            {

                countVarMappersPerInstance = 0;
                // Populate clientMapper
                addToMapper(obj, count);
                firstElmMapper[firstElmMapper.Count - 1].RemoveButton = "Button";
                count++;
            }
        }

        private void PopulateMappersClass()
        {
            // Populate clientMapper
            addToMapper(firstElm, -1);
        }

        private void addToMapper(object? obj, int index)
        {
            PropertyInfo[] clientProperties = obj?.GetType().GetProperties() ?? [];
            foreach (PropertyInfo prop in clientProperties)
            {
                VarMapper varMapper = new VarMapper();
                varMapper.Reference = index;
                varMapper.VarRealName = prop.Name;
                varMapper.VarValue = prop.GetValue(obj) ?? "";
                varMapper.ID = firstElmMapper.Count;
                long selectionDefault = 0;
                string selectionVariables = "";
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    switch (attr)
                    {
                        case CustomDescriptionAttribute att: varMapper.VarName = att.Description; break;
                        case DefaultStateAttribute att:
                            Type def = att.DefaultState.GetType();
                            if (varMapper.VarValue == null || 
                                varMapper.VarValue is string && string.IsNullOrEmpty(varMapper.VarValue as string) || 
                                varMapper.VarValue is not string && varMapper.VarValue is not int && 
                                varMapper.VarValue == Activator.CreateInstance(varMapper.VarValue?.GetType() ?? typeof(string)))
                                varMapper.VarValue = att.DefaultState;
                            try
                            {
                                selectionDefault = Convert.ToInt64(att.DefaultState);
                            }
                            catch { }
                            break;
                        case CustomSelectionAttribute att: selectionVariables = att.DefaultType; break;
                        case EditableAttribute att: varMapper.Editable = att.AllowEdit; break;
                        case RequiredAttribute att: varMapper.RequiredB = true; break;
                        case CustomImporterAttribute att: 
                            firstElmMapper.Add(new VarMapper { 
                                ID=firstElmMapper.Count+1,
                                Reference = index, 
                                VarType = "Importer", 
                                VarName = att.Description, 
                                SelectionOptions = att.ApplyTo.Cast<string>().ToList(),
                                VarRealName = firstElmMapper.Count+1,
                                VarValue = firstElmMapper.Count
                            }); 
                            break;
                        default:
                            break;
                    }
                }
                string namespaceStr = prop.PropertyType.Namespace ?? "System";
                Type tp = prop.PropertyType.IsGenericType ? prop.PropertyType.GetGenericTypeDefinition() : typeof(string);
                if (varMapper.VarName != null)
                {
                    if (prop.PropertyType == typeof(bool))
                        varMapper.VarType = "Boolean";
                    else if (selectionVariables != "" && bindingFunctions.TryGetValue(selectionVariables, out Func<object, object?>? fetchFunc))
                    {

                        Dictionary<long, string>? dic;
                        if (!nestedResult.TryGetValue(fetchFunc.Method.Name, out dic))
                        {
                            dic = (Dictionary<long, string>?)fetchFunc.Invoke(firstElm) ?? new Dictionary<long, string>();
                            nestedResult[fetchFunc.Method.Name] = dic;
                        }
                        varMapper.VarType = "Selection";
                        List<long> keys = dic.Keys.ToList();
                        List<string> values = dic.Values.ToList();

                        if (varMapper.Editable)
                        {
                            keys.Insert(0, -1);
                            values.Insert(0, "< Ajouter/Modifier >");
                        }

                        long indx = values.IndexOf(varMapper.VarValue?.ToString() ?? "");
                        if (varMapper.VarValue != null && varMapper.VarValue.GetType() == typeof(string) && varMapper.VarValue.ToString() != "" && indx == -1)
                        {
                            indx = keys.Count;
                            keys.Add(keys.Count);
                            values.Add(varMapper.VarValue.ToString()??"");
                        }
                        varMapper.VarValue = indx == -1? selectionDefault.ToString() : indx.ToString();

                        varMapper.selectionIndexes = keys;
                        varMapper.SelectionOptions = values;
                    }
                    else if (!namespaceStr.StartsWith("System") || prop.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(IList<>) || tp == typeof(List<>)))
                        varMapper.VarType = "Button";
                    firstElmMapper.Add(varMapper);
                    countVarMappersPerInstance++;
                }
            }
            firstElmMapper[firstElmMapper.Count - 1].LastB = true;
        }

        private void BindItemsControl()
        {
            ClientControl.ItemsSource = firstElmMapper;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.GoBack();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool validIteration = true;
            Type tp = firstElm.GetType().IsGenericType ? firstElm.GetType().GetGenericTypeDefinition() : typeof(string);
            if (firstElm.GetType().IsGenericType && (tp == typeof(ICollection) || tp == typeof(IList) || tp == typeof(List<>)))
            {
                IList ICollectionObjs = (IList)firstElm;
                int count = 0;
                int countIter = 0;
                int invalidPerIter = 0;
                foreach (var mapper in firstElmMapper)
                {
                    countIter++;
                    int countCopy = count;
                    if (countIter == countVarMappersPerInstance)
                    {
                        if (invalidPerIter == countVarMappersPerInstance)
                        {
                            ICollectionObjs?.RemoveAt(count);
                            count--;
                        }
                        invalidPerIter = 0;
                        count++;
                        countIter = 0;
                    }

                    if (mapper == null || mapper.VarType?.ToString() == "Importer")
                        continue;

                    object? obj = ICollectionObjs?[countCopy];
                    PropertyInfo? property = obj?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (mapper.VarValue == null || mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string) || mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string)))
                    {
                        invalidPerIter++;
                        if (mapper.RequiredB == true && (string)(mapper.VarType ?? "") != "Button" || mapper.RequiredB && property?.GetValue(firstElm) == null)
                        {
                            mapper.Required = "#FFE41212";
                            validIteration = false;
                        }
                        continue;

                    }
                    if (property != null)
                    {
                        string namespaceStr = property.PropertyType.Namespace ?? "System";
                        tp = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericTypeDefinition() : typeof(string);
                        if (!namespaceStr.StartsWith("System") || property.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(ICollection<>)))
                        {
                            if (mapper.VarType?.ToString() == "Selection")
                            {
                                object? obj1 = Activator.CreateInstance(property.PropertyType);
                                if (mapper.VarValue is long)
                                {
                                    obj1?.GetType().GetProperties()?.FirstOrDefault(
                                        prop =>
                                            prop.GetCustomAttributes(true).FirstOrDefault(x => x.GetType() == typeof(KeyAttribute)) != null
                                        )?.SetValue(obj1, mapper.selectionIndexes.ElementAt((Index)(long)mapper.VarValue));
                                }
                                else if (mapper.VarValue is int)
                                {
                                    obj1?.GetType().GetProperties()?.FirstOrDefault(
                                        prop =>
                                            prop.GetCustomAttributes(true).FirstOrDefault(x => x.GetType() == typeof(KeyAttribute)) != null
                                        )?.SetValue(obj1, mapper.selectionIndexes.ElementAt((Index)(int)mapper.VarValue));
                                }
                                else if (mapper.VarValue is string && obj1?.GetType() == typeof(string))
                                    obj1 = mapper.VarValue;
                                else if (mapper.VarValue is string)
                                    obj1?.GetType().GetProperties().ToList().ForEach(p =>
                                    {
                                        if (p.PropertyType == typeof(string))
                                            p.SetValue(obj1, mapper.VarValue);
                                    });
                                property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(obj1, property.PropertyType));
                            }
                            continue;
                        }
                        if (mapper.VarType?.ToString() == "Selection")
                        {
                            if (mapper.VarValue is long)
                                property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(mapper.SelectionOptions.ElementAt((Index)(long)mapper.VarValue), property.PropertyType));
                            else if (mapper.VarValue is int)
                                property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(mapper.SelectionOptions.ElementAt((Index)(int)mapper.VarValue), property.PropertyType));
                            else if (mapper.VarValue is string)
                                property.SetValue(ICollectionObjs?[countCopy], mapper.VarValue);
                            continue;
                        }
                        if (property.PropertyType == typeof(decimal))
                            mapper.VarValue = mapper.VarValue?.ToString()?.Replace(".", ",") ?? "";
                        property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(mapper?.VarValue, property.PropertyType));
                    }
                }
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];

                if (ICollectionObjs?.Count == 1 && (ICollectionObjs[0]?.Equals(Activator.CreateInstance(innerType)) ?? false))
                    ICollectionObjs.Clear();
                for (int k = 0; k < ICollectionObjs?.Count; k++)
                {
                    if (k > count)
                    {
                        ICollectionObjs.RemoveAt(k);
                        k--;
                    }
                    k++;
                }
                firstElm = ICollectionObjs ?? firstElm;
            }
            else
            {
                foreach (var mapper in firstElmMapper)
                {
                    if (mapper.VarType?.ToString() == "Importer")
                        continue;
                    var property = firstElm?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (mapper.VarValue == null || mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string) || mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string)))
                    {
                        if (mapper.RequiredB == true && (string)(mapper.VarType ?? "") != "Button" || mapper.RequiredB && property?.GetValue(firstElm) == null)
                        {
                            mapper.Required = "#FFE41212";
                            validIteration = false;
                            continue;
                        }

                    }
                    if (property != null)
                    {
                        string namespaceStr = property.PropertyType.Namespace ?? "System";
                        tp = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericTypeDefinition() : typeof(string);
                        if (!namespaceStr.StartsWith("System") || property.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(ICollection<>)))
                        {
                            if (mapper.VarType?.ToString() == "Selection") { 
                                object? obj1 = Activator.CreateInstance(property.PropertyType);

                                if (mapper.VarValue is long)
                                {
                                    obj1?.GetType().GetProperties()?.FirstOrDefault(
                                        prop =>
                                            prop.GetCustomAttributes(true).FirstOrDefault(x => x.GetType() == typeof(KeyAttribute)) != null
                                        )?.SetValue(obj1, mapper.selectionIndexes.ElementAt((Index)(long)mapper.VarValue));
                                }
                                else if( mapper.VarValue is int)
                                {
                                    obj1?.GetType().GetProperties()?.FirstOrDefault(
                                        prop =>
                                            prop.GetCustomAttributes(true).FirstOrDefault(x=> x.GetType() == typeof(KeyAttribute)) != null
                                        )?.SetValue(obj1, mapper.selectionIndexes.ElementAt((Index)(int)mapper.VarValue));
                                }
                                else if (mapper.VarValue is string)
                                    obj1?.GetType().GetProperties().ToList().ForEach(p => {
                                        if (p.PropertyType == typeof(string))
                                            p.SetValue(obj1, mapper.VarValue);
                                    });
                                property.SetValue(firstElm, Convert.ChangeType(obj1, property.PropertyType));
                            }
                            continue;
                        }
                        if (mapper.VarType?.ToString() == "Selection")
                        {
                            if (mapper.VarValue is long)
                                property.SetValue(firstElm, Convert.ChangeType(mapper.SelectionOptions.ElementAt((Index)(long)mapper.VarValue), property.PropertyType));
                            else if (mapper.VarValue is int)
                                property.SetValue(firstElm, Convert.ChangeType(mapper.SelectionOptions.ElementAt((Index)(int)mapper.VarValue), property.PropertyType));
                            else if (mapper.VarValue is string)
                                property.SetValue(firstElm, mapper.VarValue);
                            continue;
                        }
                        if (property.PropertyType == typeof(decimal))
                            mapper.VarValue = mapper.VarValue?.ToString()?.Replace(".", ",") ?? "";
                        property.SetValue(firstElm, Convert.ChangeType(mapper.VarValue, property.PropertyType));
                    }
                }

            }
            if (!validIteration)
            {
                ClientControl.ItemsSource = new List<string>();
                ClientControl.ItemsSource = firstElmMapper;
                return;
            }

            if (bindingFunctions.TryGetValue("postProcessingFunction", out var postProcessingFunction))
                firstElm = postProcessingFunction.Invoke(firstElm ?? "") ?? "";
            if (bindingFunctions.TryGetValue("saveFunction", out var saveFunc))
                saveFunc.Invoke(firstElm ?? "");
            end?.Invoke();
            NavigationService.GoBack();
        }

        private void AddICollectionButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            string? tag = btn.CommandParameter?.ToString();
            if (tag != null)
            {
                Type type = firstElm.GetType();
                PropertyInfo? prop = type.GetProperty(tag);
                if (prop != null)
                {
                    Type propType = prop.PropertyType;
                    tempPropertyName = prop.Name;
                    Dictionary<string, Func<object, object?>> instanceDic = bindingFunctions.ToDictionary(entry => entry.Key,
                                                                                                         entry => entry.Value);
                    instanceDic["saveFunction"] = MergeICollectionEntry;

                    NavigationService.Content = new ItemEditorPage(prop.GetValue(firstElm) ?? Activator.CreateInstance(propType) ?? "", instanceDic, prop.Name, null);
                }
            }
        }

        private void RemoveICollectionEntryButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            IList ICollectionObjs = (IList)firstElm;
            int tag = int.Parse(btn.CommandParameter.ToString() ?? "-99999999999");

            while (firstElmMapper.FirstOrDefault(x => x.Reference == tag) != null)
            {
                VarMapper varMapper = firstElmMapper.First(x => x.Reference == tag);
                firstElmMapper.Remove(varMapper);
            }

            firstElmMapper.Select(x =>
            {
                if (x.Reference > tag)
                    x.Reference--;
                return x;
            });
            if (tag < ICollectionObjs.Count)
            {
                ICollectionObjs.RemoveAt(tag);
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];


                if (ICollectionObjs?.Count == 1 && (ICollectionObjs[0]?.Equals(Activator.CreateInstance(innerType)) ?? false))
                    ICollectionObjs.Clear();

                firstElm = ICollectionObjs ?? firstElm;
            }
        }

        private void AddICollectionEntryButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the inner object type
            Type innerType = firstElm.GetType().GetGenericArguments()[0];

            // Create an instance of the inner object type
            try
            {
                object? instance = Activator.CreateInstance(innerType);

                IList ICollectionObjs = (IList)firstElm;
                ICollectionObjs.Add(instance ?? throw new Exception());
                firstElm = ICollectionObjs;
                countVarMappersPerInstance = 0;
                addToMapper(instance, firstElmMapper.Last().Reference + 1);
                firstElmMapper[firstElmMapper.Count - 1].RemoveButton = "Button";
            }
            catch
            {
            }
        }

        private object? MergeICollectionEntry(object obj)
        {

            Type type = firstElm.GetType();
            PropertyInfo? prop = type.GetProperty(tempPropertyName);
            Type propType = obj.GetType().IsGenericType ? obj.GetType().GetGenericTypeDefinition() : obj.GetType();
            if (propType.IsGenericType && (propType == typeof(ICollection) || propType == typeof(IList) || propType == typeof(List<>)))
            {
                prop?.SetValue(firstElm, obj);
            }
            else
            {
                prop?.SetValue(firstElm, Convert.ChangeType(obj, prop.PropertyType));
            }
            return obj;
        }

        private void ImporterButton_Click(object sender, RoutedEventArgs e)
        {

            Button btn = (Button)sender;
            int tag = int.Parse(btn.Tag?.ToString() ?? "0");
            VarMapper? importer = firstElmMapper.FirstOrDefault(x => x.ID == tag);
            if (importer == null)
                return;
            VarMapper? reference = firstElmMapper.FirstOrDefault(y => y.ID == int.Parse(importer.VarValue?.ToString() ?? "0"));
            if (reference == null || !bindingFunctions.TryGetValue(reference.VarRealName + "_Importer", out var importerFunc))
                return;
            object? res = importerFunc.Invoke(reference?.VarValue ?? "");
            if (res == null){

                foreach (VarMapper x in firstElmMapper)
                {
                    if (x.ID == reference?.ID)
                        x.VarValue = "Erreur, veuillez réessayer";
                };
                ClientControl.ItemsSource = new List<string>();
                ClientControl.ItemsSource = firstElmMapper;
                return;
            }
            Type type = res.GetType();
            foreach (string importerVal in importer.SelectionOptions)
            {
                PropertyInfo? prop = type.GetProperty(importerVal);
                if (prop?.GetValue(res) != null)
                    foreach(VarMapper x in firstElmMapper)
                    {
                        if (x.Reference == importer.Reference && x.VarRealName?.ToString() == importerVal)
                        {

                            Type tp = firstElm.GetType().IsGenericType ? firstElm.GetType().GetGenericTypeDefinition() : typeof(string);
                            if (firstElm.GetType().IsGenericType && (tp == typeof(ICollection) || tp == typeof(IList) || tp == typeof(List<>)))
                            {
                                IList? ICollectionObjs = prop.GetValue(firstElm) == null ? (IList?)Activator.CreateInstance(prop.PropertyType) : (IList?)prop.GetValue(firstElm);
                                IList? collection1 = (IList?)prop.GetValue(res);
                                foreach(var y in collection1??((IList)new List()))
                                {
                                    ICollectionObjs?.Add(y);
                                }
                                prop.SetValue(firstElm, ICollectionObjs);
                            }
                            else
                            {
                                x.VarValue = prop.GetValue(res);
                            }
                        }
                    };
            }
            ClientControl.ItemsSource = new List<string>();
            ClientControl.ItemsSource = firstElmMapper;
        }

        private void ComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            if ((bool)textBox.Tag == false || int.TryParse(textBox.Text, out int id) && id != 0)
                textBox.Visibility = Visibility.Collapsed;
            else
                textBox.Visibility = Visibility.Visible;
        }
    }
}

