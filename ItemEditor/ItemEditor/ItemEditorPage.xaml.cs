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

namespace ItemEditor
{
    public class VarMapper
    {
        private string _name {  get; set; }
        public string? VarName { get { return RequiredB ? (_name ?? "") + " *" : _name; } set { _name = value??""; } }
        public object? VarValue { get; set; }
        public object? VarRealName { get; set; }
        public object? VarType { get; set; }
        public int Reference { get; set; }
        public string RemoveButton { get; set; }
        public ICollection<int> selectionIndexes { get; set; }
        public ICollection<string> SelectionOptions { get; set; }

        public string Required { get; set; }
        private bool _required {  get; set; }
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
            selectionIndexes = new List<int>();
            SelectionOptions = new List<string>();
        }
    }
    /// <summary>
    /// Logique d'interaction pour ItemEditorPage.xaml
    /// </summary>
    public partial class ItemEditorPage : Page
    {
        private readonly ObservableCollection<VarMapper> firstElmMapper = new ObservableCollection<VarMapper>();
        private object firstElm;
        private readonly Func<object, object> saveObjFunc;
        private readonly Func<object, object>? postProcessingFunction;
        private readonly Action? end;
        private string tempPropertyName = "";
        int countVarMappersPerInstance = 0;
        private readonly bool secondPrioritary;

        public ItemEditorPage(object firstElm, Func<object, object> saveFunction, Func<object, object>? postProcessingFunction = null, string pageTitle = "", Action? end = null)
        {
            InitializeComponent();
            PageTitle.Text = pageTitle;
            this.firstElm = firstElm;
            this.end = end;
            saveObjFunc = saveFunction;
            this.postProcessingFunction = postProcessingFunction;

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
                catch (Exception ex)
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

                string selectionVariables = "";
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    switch (attr)
                    {
                        case CustomDescriptionAttribute att: varMapper.VarName = att.Description;break;
                        case DefaultStateAttribute att:
                            if (varMapper.VarValue == null || varMapper.VarValue is string && string.IsNullOrEmpty(varMapper.VarValue as string) || varMapper.VarValue is not string && varMapper.VarValue is not int && varMapper.VarValue == Activator.CreateInstance(varMapper.VarValue?.GetType() ?? typeof(string)))
                                varMapper.VarValue = att.DefaultState; break;
                        case CustomSelectionAttribute att: selectionVariables = att.DefaultType; break;
                        case RequiredAttribute att: varMapper.RequiredB = true;break;
                        default:
                            break;
                    }
                }
                string namespaceStr = prop.PropertyType.Namespace ?? "System";
                Type tp = prop.PropertyType.IsGenericType ? prop.PropertyType.GetGenericTypeDefinition() : typeof(string);
                if (varMapper.VarName != null)
                {
                    if (selectionVariables != "")
                    {
                        Dictionary<int, string>? dic = (Dictionary<int, string>?)obj?.GetType().GetProperty(selectionVariables)?.GetValue(obj);
                        if (dic != null)
                        {
                            varMapper.VarType = "Selection";
                            varMapper.VarValue = 0;
                            varMapper.selectionIndexes = dic.Keys.ToList();
                            varMapper.SelectionOptions = dic.Values.ToList();
                        }

                    }
                    else if (!namespaceStr.StartsWith("System") || prop.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(IList<>) || tp == typeof(List<>)))
                        varMapper.VarType = "Button";
                    firstElmMapper.Add(varMapper);
                    countVarMappersPerInstance++;
                }
            }
            firstElmMapper[firstElmMapper.Count - 1].LastB = true;
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
                    NavigationService.Content = new ItemEditorPage(prop.GetValue(firstElm) ?? Activator.CreateInstance(propType) ?? "", MergeICollectionEntry, null, prop.Name, null);
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
                countVarMappersPerInstance--;
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

        private object MergeICollectionEntry(object obj)
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
                    if (mapper.VarValue == null || mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string) || mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string)))
                    {
                        invalidPerIter++;
                        if (mapper.RequiredB == true)
                        {
                            mapper.Required = "#FFE41212";
                            validIteration = false;
                            continue;
                        }
                    }
                    object? obj = ICollectionObjs?[count];
                    PropertyInfo? property = obj?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (property != null)
                    {
                        if (property.PropertyType == typeof(decimal))
                            mapper.VarValue = mapper.VarValue?.ToString()?.Replace(".", ",") ?? "0";
                        if (property.PropertyType == typeof(string))
                            mapper.VarValue = mapper.VarValue?.ToString()?.Trim();
                        try
                        {
                            property.SetValue(ICollectionObjs?[count], Convert.ChangeType(mapper.VarValue, property.PropertyType));
                        }
                        catch
                        {
                            property.SetValue(ICollectionObjs?[count], Activator.CreateInstance(property.PropertyType));
                        }
                    }
                    countIter++;
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
                }
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];

                if (ICollectionObjs?.Count == 1 && (ICollectionObjs[0]?.Equals(Activator.CreateInstance(innerType)) ?? false))
                    ICollectionObjs.Clear();

                firstElm = ICollectionObjs ?? firstElm;
            }
            else
            {
                foreach (var mapper in firstElmMapper)
                {
                    var property = firstElm?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (mapper.VarValue == null || mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string) || mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string)))
                    {
                        if (mapper.RequiredB == true && (string)(mapper.VarType??"") != "Button" || mapper.RequiredB && property?.GetValue(firstElm) == null)
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
                            if (mapper?.VarType?.ToString() == "Selection")
                            {
                                object? obj = Activator.CreateInstance(property.PropertyType);
                                obj?.GetType().GetProperty("ID")?.SetValue(obj, mapper.selectionIndexes.ElementAt((int?)mapper.VarValue ?? 0));
                                property.SetValue(firstElm, Convert.ChangeType(obj, property.PropertyType));
                            }
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
                ClientControl.ItemsSource= new List<string>();
                ClientControl.ItemsSource = firstElmMapper;
                return;
            }

            if (postProcessingFunction != null)
                firstElm = postProcessingFunction.Invoke(firstElm ?? "");
            if (firstElm != null)
                saveObjFunc.Invoke(firstElm);
            if (end != null)
                end.Invoke();
            NavigationService.GoBack();
        }
    }
}

