# ItemEditor

We all got this badly written but super useful code, here is mine.
Powerful, Time Savy and fast but unreadable by anyone else than god and myself.
I hope it can help you save some time from creating modules for each and every Class that needs a CRUD in your code.

Let's break down what you can do and use this code for :

`
public ItemEditorPage(object firstElm, Dictionary<string, Func<object, object?>> bindingFunctions, string pageTitle = "", Action? end = null)
`
## Object firstElm

The value you want to pass to be edited. It can be any classes or Enums etc...
Each editable variable needs at least one of the following attributes to be shown in the editor.

Here are all the attributes you can use and their specifics  :
```bash
    CustomDescriptionAttribute(string)
    => to use on the $class or on a $variable
    =  The message that will be displayed above the input field.

    DefaultStateAttribute(value)
    => to use on a $variable
    =  This will add a default value

    CustomSelectionAttribute(string)
    => to use on a $variable
    =  This will call the $function in $bindingFunctions that match the string
    
    EditableAttribute(bool)
    => to use on a $variable with $CustomSelectionAttribute
    =  This will add a choice in the dropdown that will make a Textbox appear for the user to write his own result
    
    BrowsableAttribute
    => to use on a $string_variable
    =  This will change the field to a button that opens a directory selector
    
    FileExtensionsAttribute
    => to use on a $string_variable
    =  This will change the field to a button that opens a file selector
    
    RequiredAttribute
    => to use on a $variable
    =  This will lock the save button until the field has a value

    ObfuscationAttribute
    => to use on a $variable
    =  This will make the input field hide letters with * appearing instead.

    CustomImporterAttribute(string, List<string>)
    => to use on a $variable
    =  This will add a separate block above with:
    =  an input field
    =  a title that has the string parameter as text
    =  a button that will fire the [PropertyName]_Importer interruptor.
    =  the List<string> should contain the name of all the variables that will be overriden by the
    =  result of the interruptor (for more info, see below).
```

## bindingFunctions

You can refer to the following bindingFunctions dictionary to see how you call them.

```cs

public class Test {
    [CustomDescription("Username (Cannot be modified)")]
    public string Username { get; set; }
    [CustomDescription("Email")]
    public string Email { get; set; }
    [CustomSelection("AgesList")]
    public string Age { get; set; }
}
public class B {
    public void anyfunc()
    {

        Dictionary<string, Func<object, object?>> inst = new()
        {
            { "saveFunction", QueryManager.PushTest },
            { "AgesList", GetAvailableAgesList },
        };
        TestFrame.Content = new ItemEditorPage(new Test(), inst, .....
    }
}
```

### [PropertyName]_Importer :

Activate upon the addition of any Field Module with the attribute [CustomImporter("import test", ["Email", ...])]

This will call the function bound to it in the bindingFunctions.
This function should return an object of the same type as the class (Test in our example).
It will automatically set the value of any field added in the list<string> of the attribute.
It will not update the selected value shown on screen if a dropdown selection is mentioned.

### CustomSelection Interruptor :

Activate upon the addition of any Selection Module with the attribute [CustomSelection("xxx")].

It should return a string list.
The value to assign (string Age) can automatically be assigned if is an int or a string. If not, the selector will try to assign it to any other variable of type string.


### [PropertyName]_selected_selector_value :

If selected value is not of type int, you may have to use this interruptor to select the selected value in the Selector

Ex :
Age is a string, you should call this interruptor and choose the index value based on the string list that you returned with "AgesList".

### [PropertyName]_value_cast :

An interruptor that activates just before the assignment of the variable. Useful to select in a dropdown with another end type than int.

Ex : 
You got 
```cs
private enum Example {
    a,
    b
}
```

And you want to have a dropdown between a and b. The returned value upon saving will be an int. The int cannot be directly assigned or Converted to the Enum Variable via reflection. The solution is to interrupt the assignment and handle the casting

```cs
private object IntToExample(object? obj)
{
    if (obj is int)
    {
        return (Example)obj;
    }
    return obj;
}
```

It activates on all values so prefer the next postProcessingFunction interruptor if you just want to impact one value that is not subject to a crashing problem like wrong type cast. 

### postProcessingFunction :

An interruptor that activates at the end of the process of variables assignment. It can be used for anything you would want to add before the final invocation.
Ex:
You have a value that you do not want to be editable but need it for the creation of the instance :

```cs
public class X 
{
    public Test t { get; set; }
    
    public void createTest()
    {
        Dictionary<string, Func<object, object?>> inst = new()
        {
            { "saveFunction", QueryManager.PushTest },
        };
        TestFrame.Content = new ItemEditorPage(new Company(), inst, "Add  a Test");
    }
    
    public void editTest(Test test)
    {
        Dictionary<string, Func<object, object?>> inst = new()
        {
            { "saveFunction", QueryManager.PushTest },
            { "postProcessingFunction", blockUsernameEdition }
        };
        TestFrame.Content = new ItemEditorPage(new Company(), inst, "Modify  a Test");
    }

    public object blockUsernameEdition(object? obj)
    {
        Test test = (obj as Test) ?? new();
        test.Username = t.Username;
        return test;
    }
}
```

### saveFunction :

The interruptor that will return to you the value of your object that you can use to save or do anything with.

## Page Title

Default Page Title that you want to apply. Is overriden by a custom description on the class

## End Invocation

End is the very last function that will be called, it can be used to affect the return page or any other action that would be needed to cleanup.
Ex : Call a refresh function that will fetch the latest changes applied to the database.