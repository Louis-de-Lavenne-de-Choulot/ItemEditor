using CustomItemEditorAttributes;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace ItemEditor
{
    public class VarMapper
    {
        public int ID { get; set; }
        private string? _name { get; set; }
        public string? VarName { get => RequiredB ? (_name ?? "") + " *" : _name; set => _name = value ?? ""; }
        public object? VarValue { get; set; }
        public object? VarRealValue { get; set; }
        public object? VarRealName { get; set; }
        public object? VarType { get; set; }
        public int Reference { get; set; }
        public string RemoveButton { get; set; }
        public ICollection<object> selectionIndexes { get; set; }
        public ICollection<string> SelectionOptions { get; set; }
        public bool Editable { get; set; } = false;
        public Action<object, RoutedEventArgs>? EditFunc { get; set; }
        public string? Required { get; set; }
        private bool _required { get; set; }
        public bool RequiredB
        {
            get => _required;
            set
            {
                Required = "#FF000000";
                _required = value;
            }
        }
        public string Last { get; set; } = "";
        public bool LastB
        {
            get => Last != "";
            set => Last = value ? "Y" : "";
        }
        public Visibility DropdownVisibility { get; set; } = Visibility.Collapsed;
        public VarMapper()
        {
            LastB = false;
            RequiredB = false;
            Reference = 0;
            RemoveButton = string.Empty;
            selectionIndexes = [];
            SelectionOptions = [];
        }
    }

    /// <summary>
    /// Logique d'interaction pour ItemEditorPage.xaml
    /// </summary>
    public partial class ItemEditorPage : Page
    {
        private readonly ObservableCollection<VarMapper> firstElmMapper = [];
        private readonly Dictionary<string, Func<object, object?>> bindingFunctions = [];
        private readonly Dictionary<string, Dictionary<object, string>> nestedResult = [];
        private object firstElm;
        bool isEnumWithFlag = false;
        private readonly Action? end;
        private string tempPropertyName = "";
        private int countVarMappersPerInstance = 0;
        private static CultureInfo culture = CultureInfo.InvariantCulture;
        private readonly Dictionary<Type, Func<object, (object, bool)>> TypeValidator = new()
        {
            {typeof(bool), s => {return (s, true); } },
            {typeof(string), s => {return (s, true); } },
            {typeof(decimal), s => {
                s = s?.ToString()?.Replace(",", ".") ?? "";
                return Regex.IsMatch(s.ToString() ?? "", "^[0-9]+(\\.[0-9]+)?$") ? ((object, bool))(s, true) : ((object, bool))(s, false); } },
            {typeof(double), s => {
                s = s?.ToString()?.Replace(",", ".") ?? "";
                return Regex.IsMatch(s.ToString() ?? "", "^[0-9]+(\\.[0-9]+)?$")  ? ((object, bool))(s, true) : ((object, bool))(s, false); } },
            {typeof(float), s => {
                s = s?.ToString()?.Replace(",", ".") ?? "";
                return Regex.IsMatch(s.ToString() ?? "", "^[0-9]+(\\.[0-9]+)?$")   ? ((object, bool))(s, true) : ((object, bool))(s, false); } },
            {typeof(int), s => {return (s, int.TryParse(s.ToString(),  out _) ); } },
            {typeof(long), s => {return (s, long.TryParse(s.ToString(), out _) ); } }
        };
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
            {
                PopulateMappersClass();
            }

            foreach (object attr in attrs)
            {
                if (attr is CustomDescriptionAttribute descriptionAttribute)
                {
                    PageTitle.Text = descriptionAttribute.Description;
                }
                if (attr is FlagsAttribute flagsAttribute)
                {
                    isEnumWithFlag = true;
                }
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
                    _ = ICollectionObjs.Add(instance ?? throw new Exception());
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
                firstElmMapper[^1].RemoveButton = "Button";
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

            MemberInfo[] clientMethods = obj?.GetType().GetMembers(BindingFlags.Public | BindingFlags.Static) ?? [];
            PropertyInfo[] clientProperties = obj?.GetType().GetProperties() ?? [];
            addToMapperLoop(obj, index, clientMethods);
            addToMapperLoop(obj, index, clientProperties);
        }   


        private void addToMapperLoop(object? obj, int index, dynamic props)
        {
            bool propsIsMember = props.GetType() == typeof(MemberInfo[]);
            foreach (dynamic prop in props)
            {
                VarMapper varMapper = new()
                {
                    Reference = index,
                    VarRealName = prop.Name,
                    VarValue = propsIsMember ? false : prop.GetValue(obj) ?? "",
                    ID = firstElmMapper.Count
                };
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
                                (varMapper.VarValue is string && string.IsNullOrEmpty(varMapper.VarValue as string)) ||
                                (varMapper.VarValue is not string && varMapper.VarValue is not int &&
                                varMapper.VarValue == Activator.CreateInstance(varMapper.VarValue?.GetType() ?? typeof(string))))
                            {
                                varMapper.VarValue = att.DefaultState;
                            }

                            try
                            {
                                selectionDefault = Convert.ToInt64(att.DefaultState);
                            }
                            catch { }
                            break;
                        case CustomSelectionAttribute att: selectionVariables = att.DefaultType; break;
                        case EditableAttribute att: varMapper.Editable = att.AllowEdit; break;
                        case BrowsableAttribute: varMapper.VarType = "FolderSelection"; break;
                        case FileExtensionsAttribute: varMapper.VarType = "FileSelection"; break;
                        case RequiredAttribute att: varMapper.RequiredB = true; break;
                        case CustomImporterAttribute att:
                            firstElmMapper.Add(new VarMapper
                            {
                                ID = firstElmMapper.Count + 1,
                                Reference = index,
                                VarType = "Importer",
                                VarName = att.Description,
                                SelectionOptions = att.ApplyTo.Cast<string>().ToList(),
                                VarRealName = firstElmMapper.Count + 1,
                                VarValue = firstElmMapper.Count
                            });
                            break;
                        default:
                            break;
                    }
                }
                string namespaceStr = propsIsMember ? "System" : prop.PropertyType.Namespace ?? "System";
                Type tp = propsIsMember ? prop.DeclaringType : prop.PropertyType.IsGenericType ? prop.PropertyType.GetGenericTypeDefinition() : typeof(string);
                if (varMapper.VarName != null)
                {
                    if (propsIsMember || prop.PropertyType == typeof(bool))
                    {
                        Enum.TryParse(prop.DeclaringType, prop.Name, out dynamic res);
                        if (propsIsMember)
                        {
                            if (obj?.GetType().GetCustomAttribute<FlagsAttribute>() != null)
                            {
                                varMapper.VarValue = ((Enum)firstElm).HasFlag(res);
                                varMapper.VarRealValue = (int)res;
                            }
                        }
                        
                        varMapper.VarType = "Boolean";
                    }
                    else if (selectionVariables != "" && bindingFunctions.TryGetValue(selectionVariables, out Func<object, object?>? fetchFunc))
                    {
                        object resultObj = "";
                        Dictionary<object, string>? dic = [];
                        if (!nestedResult.TryGetValue(fetchFunc.Method.Name, out dic))
                        {
                            dic = [];
                            resultObj = fetchFunc.Invoke(firstElm) ?? new List<string>();
                            IList tempList = (IList)resultObj;

                            for (int i = 0; i < tempList.Count; i++)
                            {
                                if (dic.ContainsKey(tempList[i]??""))
                                {
                                    continue;
                                }

                                dic.Add(tempList[i]??"", tempList[i]?.ToString() ?? "ERR NO TEXT");
                            }

                            nestedResult[fetchFunc.Method.Name] = dic;
                        }
                        varMapper.VarType = "Selection";
                        List<object> keys = dic.Keys.ToList();
                        List<string> values = dic.Values.ToList();

                        if (varMapper.Editable)
                        {
                            keys.Insert(0, -1);
                            values.Insert(0, "< Ajouter/Modifier >");
                        }
                        keys.Insert(0, -2);
                        values.Insert(0, "Aucune");

                        bool selectedF = bindingFunctions.TryGetValue(varMapper.VarRealName + "_selected_selector_value", out Func<object, object?>? selectedFunc);
                        KeyValuePair<object, object> kvp = new(values, varMapper.VarValue);
                        long indx = selectedF ? (long?)selectedFunc?.Invoke(kvp) ?? 0 : values.IndexOf(varMapper.VarValue?.ToString() ?? "");
                        if (indx == -1 && varMapper.VarValue != null && varMapper.VarValue.GetType() == typeof(string) && varMapper.VarValue.ToString() != "")
                        {
                            indx = keys.Count;
                            keys.Add(keys.Count);
                            values.Add(varMapper.VarValue.ToString() ?? "");
                        }
                        else if (indx == -1 && varMapper.VarValue != null && (!varMapper.VarValue.GetType().Namespace?.StartsWith("System") ?? false))
                        {
                            indx = (long?)varMapper.VarValue.GetType().GetProperties().FirstOrDefault(x => x.GetCustomAttribute(typeof(KeyAttribute), false) != null)?.GetValue(varMapper.VarValue) ?? -1;
                            object? val = null;
                            try
                            {
                                val = keys[(Index)indx];
                            }
                            catch { }
                            if (val == null)
                            {
                                keys.Add(varMapper.VarValue);
                                values.Add((string?)varMapper.VarValue.GetType().GetProperties().FirstOrDefault(x => x.PropertyType == typeof(string))?.GetValue(varMapper.VarValue) ?? "");
                            }
                        }

                        varMapper.VarValue = indx == -1 ? selectionDefault.ToString() : indx.ToString();

                        varMapper.selectionIndexes = keys;
                        varMapper.SelectionOptions = values;

                    }
                    else if (varMapper.VarType?.ToString() == "FolderSelection")
                    {
                        varMapper.EditFunc = FolderSelectButton_Click;
                    }
                    else if (varMapper.VarType?.ToString() == "FileSelection")
                    {
                        varMapper.EditFunc = FileSelectButton_Click;
                    }
                    else if (!namespaceStr.StartsWith("System") || (prop.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(IList<>) || tp == typeof(List<>))))
                    {
                        varMapper.VarType = "Button";
                        varMapper.EditFunc = AddICollectionButton_Click;
                    }
                    firstElmMapper.Add(varMapper);
                    countVarMappersPerInstance++;
                }
            }
            if (firstElmMapper.Count > 0)
            {
                firstElmMapper[^1].LastB = true;
            }
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
                foreach (VarMapper mapper in firstElmMapper)
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
                    mapper.Required = "#FF000000";
                    if (mapper == null || mapper.VarType?.ToString() == "Importer")
                    {
                        continue;
                    }

                    object? obj = ICollectionObjs?[countCopy];
                    PropertyInfo? property = obj?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (mapper.VarValue == null || (mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string)) || (mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string))))
                    {
                        invalidPerIter++;
                        if ((mapper.RequiredB == true && (string)(mapper.VarType ?? "") != "Button") || (mapper.RequiredB && property?.GetValue(firstElm) == null))
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
                        if (!namespaceStr.StartsWith("System") || (property.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(ICollection<>))))
                        {
                            if (mapper.VarType?.ToString() == "Selection")
                            {
                                object? obj1 = Activator.CreateInstance(property.PropertyType);
                                bool isAnInt = int.TryParse(mapper.VarValue?.ToString() ?? "", out int resultingInt);
                                if (mapper.VarValue is string && !isAnInt)
                                {
                                    obj1 = mapper.VarValue;
                                }
                                else if (resultingInt >= 0)
                                {
                                    bool isAnInt2 = int.TryParse(mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0).ToString(), out int resultingInt2);
                                    if ((isAnInt2 && resultingInt2 != -2) || !isAnInt2)
                                    {
                                        obj1 = mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0);
                                    }
                                }
                                else if (resultingInt == -1)
                                {
                                    obj1?.GetType()?.GetProperties().ToList().ForEach(x =>
                                    {
                                        if (x.PropertyType == typeof(string))
                                        {
                                            x.SetValue(obj1, mapper.VarValue?.ToString());
                                        }

                                        return;
                                    });
                                }
                                property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(obj1, property.PropertyType, culture));
                            }
                            continue;
                        }
                        else if (mapper.VarType?.ToString() == "Selection")
                        {
                            bool isAnInt = int.TryParse(mapper.VarValue?.ToString() ?? "", out int resultingInt);
                            if (mapper.VarValue is string && !isAnInt)
                            {
                                property.SetValue(ICollectionObjs?[countCopy], mapper.VarValue);
                            }
                            else if (resultingInt >= 0)
                            {
                                bool isAnInt2 = int.TryParse(mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0).ToString(), out int resultingInt2);
                                if (((isAnInt2 && resultingInt2 != -2) || !isAnInt2) && ICollectionObjs != null)
                                        ICollectionObjs[countCopy] = mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0);
                            
                        }
                            else if (resultingInt == -1)
                            {
                                ICollectionObjs?[countCopy]?.GetType()?.GetProperties().ToList().ForEach(x =>
                                {
                                    if (x.PropertyType == typeof(string))
                                    {
                                        x.SetValue(ICollectionObjs?[countCopy], mapper.VarValue?.ToString());
                                    }

                                    return;
                                });
                            }
                            continue;
                        }

                        if (TypeValidator.ContainsKey(property.PropertyType))
                        {
                            (object, bool) resultingVal = TypeValidator[property.PropertyType](mapper.VarValue ?? "");
                            if (!resultingVal.Item2)
                            {
                                invalidPerIter++;
                                mapper.Required = "#FFE41212";
                                validIteration = false;
                            }
                            else
                            {
                                property.SetValue(ICollectionObjs?[countCopy], Convert.ChangeType(resultingVal.Item1, property.PropertyType, culture));
                            }
                        }

                    }
                }
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];

                if (ICollectionObjs?.Count == 1 && (ICollectionObjs[0]?.Equals(Activator.CreateInstance(innerType)) ?? false))
                {
                    ICollectionObjs.Clear();
                }

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
                if (firstElm.GetType().BaseType == typeof(Enum))
                    firstElm = Activator.CreateInstance(firstElm.GetType()) ?? new();
                foreach (VarMapper mapper in firstElmMapper)
                {
                    if (mapper.VarType?.ToString() == "Importer")
                    {
                        continue;
                    }

                    mapper.Required = "#FF000000";
                    PropertyInfo? property = firstElm?.GetType().GetProperty(mapper.VarRealName?.ToString() ?? "");
                    if (mapper.VarValue == null || (mapper.VarValue is string && string.IsNullOrEmpty(mapper.VarValue as string)) || (mapper.VarValue is not string && mapper.VarValue is not int && mapper.VarValue == Activator.CreateInstance(mapper.VarValue?.GetType() ?? typeof(string))))
                    {
                        if ((mapper.RequiredB == true && (string)(mapper.VarType ?? "") != "Button") || (mapper.RequiredB && property?.GetValue(firstElm) == null))
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
                        if (!namespaceStr.StartsWith("System"))
                        {
                            if (mapper.VarType?.ToString() == "Selection")
                            {
                                object? obj1 = Activator.CreateInstance(property.PropertyType);
                                bool isAnInt = int.TryParse(mapper.VarValue?.ToString() ?? "", out int resultingInt);

                                if (bindingFunctions.TryGetValue(mapper.VarRealName + "_value_cast", out var func))
                                    obj1 = func.Invoke(mapper.VarValue ?? "");
                                else if (mapper.VarValue is string && obj1?.GetType() == typeof(string) && !isAnInt)
                                {
                                    obj1 = mapper.VarValue;
                                }
                                else if (resultingInt >= 0)
                                {
                                    bool isAnInt2 = int.TryParse(mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0).ToString(), out int resultingInt2);
                                    if ((isAnInt2 && resultingInt2 != -2) || !isAnInt2)
                                    {
                                        obj1 = mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0);
                                    }
                                }
                                else if (resultingInt == -1)
                                {
                                    obj1?.GetType()?.GetProperties().ToList().ForEach(x =>
                                    {
                                        if (x.PropertyType == typeof(string))
                                        {
                                            x.SetValue(obj1, mapper.VarValue?.ToString());
                                        }

                                        return;
                                    });
                                }
                                property.SetValue(firstElm, Convert.ChangeType(obj1, property.PropertyType, culture));
                            }
                            continue;
                        }
                        if (property.PropertyType.IsGenericType && (tp == typeof(ICollection<>) || tp == typeof(ICollection<>)))
                        {

                            bool isAnInt = int.TryParse(mapper.VarValue?.ToString() ?? "", out int resultingInt);
                            if (!isAnInt)
                            {
                                property.SetValue(firstElm, mapper.VarValue);
                            }
                            else
                            {
                                bool isAnInt2 = int.TryParse(mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0).ToString(), out int resultingInt2);
                                if ((isAnInt2 && resultingInt2 != -2) || !isAnInt2)
                                {
                                    property.SetValue(firstElm, mapper.selectionIndexes.ElementAt((Index)resultingInt));
                                }
                            }
                            continue;
                        }
                        if (mapper.VarType?.ToString() == "Selection")
                        {
                            bool isAnInt = int.TryParse(mapper.VarValue?.ToString() ?? "", out int resultingInt);
                            if (bindingFunctions.TryGetValue(mapper.VarRealName + "_value_cast", out var func))
                                property.SetValue(firstElm, func.Invoke(mapper.VarValue ?? ""));
                            else if (mapper.VarValue is string && !isAnInt)
                            {
                                property.SetValue(firstElm, mapper.VarValue);
                            }
                            else if (resultingInt >= 0)
                            {
                                bool isAnInt2 = int.TryParse(mapper.selectionIndexes.ElementAt((Index?)resultingInt ?? 0).ToString(), out int resultingInt2);
                                if ((isAnInt2 && resultingInt2 != -2) || !isAnInt2)
                                {
                                    property.SetValue(firstElm, mapper.selectionIndexes.ElementAt((Index)resultingInt));
                                }
                            }
                            else if (resultingInt == -1)
                            {
                                firstElm?.GetType()?.GetProperties().ToList().ForEach(x =>
                                {
                                    if (x.PropertyType == typeof(string))
                                    {
                                        x.SetValue(firstElm, mapper.VarValue?.ToString());
                                    }

                                    return;
                                });
                            }
                            continue;
                        }

                        if (TypeValidator.ContainsKey(property.PropertyType))
                        {
                            (object, bool) resultingVal = TypeValidator[property.PropertyType](mapper.VarValue ?? "");
                            if (!resultingVal.Item2)
                            {
                                mapper.Required = "#FFE41212";
                                validIteration = false;
                            }
                            else
                            {
                                property.SetValue(firstElm, Convert.ChangeType(resultingVal.Item1, property.PropertyType, culture));
                            }
                        }
                    }
                    else if (mapper.VarRealValue != null && ((bool?)mapper.VarValue ?? false))
                    {
                        firstElm = (int)firstElm!  | (int?)mapper.VarRealValue ??0;
                    }
                }

            }
            if (!validIteration)
            {
                ClientControl.ItemsSource = new List<string>();
                ClientControl.ItemsSource = firstElmMapper;
                return;
            }

            if (bindingFunctions.TryGetValue("postProcessingFunction", out Func<object, object?>? postProcessingFunction))
            {
                firstElm = postProcessingFunction.Invoke(firstElm ?? "") ?? "";
            }

            if (bindingFunctions.TryGetValue("saveFunction", out Func<object, object?>? saveFunc))
            {
                _ = saveFunc.Invoke(firstElm ?? "");
            }

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
                    object obj = prop.GetValue(firstElm) ?? Activator.CreateInstance(propType) ?? "";
                    NavigationService.Content = bindingFunctions.TryGetValue(prop.Name + "_interceptor", out Func<object, object?>? func)
                        ? new ItemEditorPage(func.Invoke(obj) ?? "", instanceDic, prop.Name, null)
                        : (object)new ItemEditorPage(obj, instanceDic, prop.Name, null);
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
                _ = firstElmMapper.Remove(varMapper);
            }

            _ = firstElmMapper.Select(x =>
            {
                if (x.Reference > tag)
                {
                    x.Reference--;
                }

                return x;
            });
            if (tag < ICollectionObjs.Count)
            {
                ICollectionObjs.RemoveAt(tag);
                // Get the inner object type
                Type innerType = firstElm.GetType().GetGenericArguments()[0];


                if (ICollectionObjs?.Count == 1 && (ICollectionObjs[0]?.Equals(Activator.CreateInstance(innerType)) ?? false))
                {
                    ICollectionObjs.Clear();
                }

                firstElm = ICollectionObjs ?? firstElm;
            }
        }

        private void FolderSelectButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            int tag = int.Parse(btn.ToolTip?.ToString() ?? "0");

            foreach (VarMapper x in firstElmMapper)
            {
                if (x.ID == tag)
                {
                    Microsoft.Win32.OpenFolderDialog dlg = new();
                    if (dlg.ShowDialog() == true)
                    {
                        x.VarValue = dlg.FolderName;
                    }
                }
            }
        }

        private void FileSelectButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            int tag = int.Parse(btn.ToolTip?.ToString() ?? "0");

            foreach (VarMapper x in firstElmMapper)
            {
                if (x.ID == tag)
                {
                    Microsoft.Win32.OpenFileDialog dlg = new()
                    {
                        Filter = "All (*.*)|*.*"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        x.VarValue = dlg.FileName;
                    }
                }
            }
        }

        private void CallFuncButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;

            int tag = int.Parse(btn.ToolTip?.ToString() ?? throw new Exception("This is mandatory, you cannot skip this exception CallFuncButton_Click"));

            foreach (VarMapper x in firstElmMapper)
            {
                if (x.ID == tag)
                {
                    x.EditFunc?.Invoke(sender, e);
                }
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
                _ = ICollectionObjs.Add(instance ?? throw new Exception());
                firstElm = ICollectionObjs;
                countVarMappersPerInstance = 0;
                addToMapper(instance, firstElmMapper.Last().Reference + 1);
                firstElmMapper[^1].RemoveButton = "Button";
            }
            catch
            {
            }
        }

        private object? MergeICollectionEntry(object obj)
        {
            if (bindingFunctions.TryGetValue(tempPropertyName + "_interceptor_end", out Func<object, object?>? func))
            {
                object? res = func.Invoke(obj);
                if (res != null)
                {
                    _ = res.GetType();
                    PropertyInfo? propI = firstElm.GetType().GetProperty(tempPropertyName);
                    propI?.SetValue(firstElm, res);
                    foreach (VarMapper item in firstElmMapper)
                    {
                        if (item?.VarRealName?.ToString() == tempPropertyName)
                        {
                            item.VarValue = res;
                        }
                    }
                }
                return obj;
            }
            Type type = firstElm.GetType();
            PropertyInfo? prop = type.GetProperty(tempPropertyName);
            Type propType = obj.GetType().IsGenericType ? obj.GetType().GetGenericTypeDefinition() : obj.GetType();
            if (propType.IsGenericType && (propType == typeof(ICollection) || propType == typeof(IList) || propType == typeof(List<>)))
            {
                prop?.SetValue(firstElm, obj);
                foreach (VarMapper item in firstElmMapper)
                {
                    if (item?.VarRealName?.ToString() == tempPropertyName)
                    {
                        item.VarValue = obj;
                    }
                }
            }
            else
            {
                if (prop?.PropertyType.BaseType == typeof(Enum))
                    prop?.SetValue(firstElm, Enum.ToObject(prop.PropertyType, obj));
                else
                    prop?.SetValue(firstElm, Convert.ChangeType(obj, prop.PropertyType, culture));
                foreach (VarMapper item in firstElmMapper)
                {
                    if (item?.VarRealName?.ToString() == tempPropertyName)
                    {
                        item.VarValue = obj;
                    }
                }
            }
            return obj;
        }

        private void ImporterButton_Click(object sender, RoutedEventArgs e)
        {

            Button btn = (Button)sender;
            int tag = int.Parse(btn.Tag?.ToString() ?? "0");
            VarMapper? importer = firstElmMapper.FirstOrDefault(x => x.ID == tag);
            if (importer == null)
            {
                return;
            }

            VarMapper? reference = firstElmMapper.FirstOrDefault(y => y.ID == int.Parse(importer.VarValue?.ToString() ?? "0"));
            if (reference == null || !bindingFunctions.TryGetValue(reference.VarRealName + "_Importer", out Func<object, object?>? importerFunc))
            {
                return;
            }

            object? res = importerFunc.Invoke(reference?.VarValue ?? "");
            if (res == null)
            {

                foreach (VarMapper x in firstElmMapper)
                {
                    if (x.ID == reference?.ID)
                    {
                        x.VarValue = "Erreur, veuillez réessayer";
                    }
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
                {
                    foreach (VarMapper x in firstElmMapper)
                    {
                        if (x.Reference == importer.Reference && x.VarRealName?.ToString() == importerVal)
                        {

                            Type tp = firstElm.GetType().IsGenericType ? firstElm.GetType().GetGenericTypeDefinition() : typeof(string);
                            if (firstElm.GetType().IsGenericType && (tp == typeof(ICollection) || tp == typeof(IList) || tp == typeof(List<>)))
                            {
                                IList? ICollectionObjs = prop.GetValue(firstElm) == null ? (IList?)Activator.CreateInstance(prop.PropertyType) : (IList?)prop.GetValue(firstElm);
                                IList? collection1 = (IList?)prop.GetValue(res);
                                foreach (object? y in collection1 ?? ((IList)new List()))
                                {
                                    _ = (ICollectionObjs?.Add(y));
                                }
                                prop.SetValue(firstElm, ICollectionObjs);
                            }
                            else
                            {
                                x.VarValue = prop.GetValue(res);
                            }
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
            textBox.Visibility = (bool)textBox.Tag == false || (int.TryParse(textBox.Text, out int id) && id != 0) ? Visibility.Collapsed : Visibility.Visible;
        }

        public class SelectedItemsConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is not IEnumerable<object> selectedItems || !selectedItems.Any() ? "Select options" : (object)string.Join(", ", selectedItems.Select(i => i.ToString()));
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
    }
}

