using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Web;
using Sitecore.Collections;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Data.Validators;
using Sitecore.Diagnostics;
using Sitecore.ExperienceEditor.Speak.Ribbon.Requests.FieldsValidation;
using Sitecore.ExperienceEditor.Speak.Server.Requests;
using Sitecore.ExperienceEditor.Speak.Server.Responses;
using Sitecore.ExperienceEditor.Switchers;
using Sitecore.Globalization;
using Sitecore.Layouts;
using Sitecore.Pipelines.GetPageEditorValidators;
using Sitecore.Pipelines.Save;
using Sitecore.Reflection;
using Sitecore.Shell;
using Sitecore.Shell.Applications.ContentEditor;
using Sitecore.Shell.Applications.ContentManager;
using Sitecore.Shell.Applications.ContentManager.ReturnFieldEditorValues;
using Sitecore.Shell.Applications.WebEdit.Commands;
using Sitecore.StringExtensions;
using Sitecore.Support.ExperienceEditor.Speak.Ribbon.Requests;
using Sitecore.Text;
using Sitecore.Web;
using Sitecore.Web.Configuration;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Sheer;
using Sitecore.Xml;
using Sitecore.CodeDom.Scripts;
using FieldEditor = Sitecore.Shell.Applications.WebEdit.Commands.FieldEditor;
using FieldEditorOptions = Sitecore.Shell.Applications.ContentEditor.FieldEditorOptions;
using FieldInfo = System.Reflection.FieldInfo;
using Sitecore.Abstractions;
using Sitecore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.Shell.Framework.Commands;



namespace Sitecore.Support.ExperienceEditor.Speak.Ribbon.Requests
{

    public class FieldValidators
    {
        internal static readonly Lazy<BaseValidatorManager> Instance = new Lazy<BaseValidatorManager>(() => ServiceLocator.ServiceProvider.GetRequiredService<BaseValidatorManager>());

        private static List<Field> ConvertToFields(IEnumerable<FieldDescriptor> fields)
        {
            List<Field> list = new List<Field>();
            foreach (FieldDescriptor current in fields)
            {
                Item item = Database.GetItem(current.ItemUri);
                if (item != null)
                {
                    Field field = item.Fields[current.FieldID];
                    if (field != null)
                    {
                        list.Add(field);
                    }
                }
            }
            return list;
        }

        private static ListString GetSuppressedRules(Item item)
        {
            string text = item["__Suppressed Validation Rules"];
            if (!string.IsNullOrEmpty(text))
            {
                return new ListString(text);
            }
            return new ListString();
        }

        public static ValidatorCollection GetFieldsValidators(ValidatorsMode mode, IEnumerable<FieldDescriptor> fields, Database database)
        {
            Assert.ArgumentNotNull(fields, "fields");
            Assert.ArgumentNotNull(database, "database");
            ValidatorCollection validatorCollection = new ValidatorCollection();
            object ob = new object();
            Type typeFromHandle = typeof(DefaultValidatorManager);

            MethodInfo method = typeFromHandle.GetMethod("ConvertToFields", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo method2 = typeFromHandle.GetMethod("BuildFieldValidators", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic, Type.DefaultBinder, new Type[]
            {
                typeof(ValidatorsMode),
                typeof(ValidatorCollection),
                typeof(IEnumerable<Field>),
               typeof(Database)
              }, null);
            MethodInfo method3 = typeFromHandle.GetMethod("GetSuppressedRules", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            List<Field> list = (List<Field>)method.Invoke(Instance.Value, new object[]
            {
                fields
            });

            method2.Invoke(Instance.Value, new object[]
            {
                mode,
                validatorCollection,
                list,
                database
            });
            List<ValidationSupression> suppressedValidators = new List<ValidationSupression>();
            foreach (FieldDescriptor current in fields)
            {
                Item item2 = Database.GetItem(current.ItemUri);
                ListString listString = (ListString)method3.Invoke(Instance.Value, new object[] { item2 });
                if (listString.Any<string>())
                {
                    suppressedValidators.Add(new ValidationSupression
                    {
                        ItemId = item2.ID.ToString(),
                        FieldId = current.FieldID.ToString(),
                        ValidatorId = string.Join(";", listString)
                    });
                }
            }

            if (suppressedValidators.Count > 0)
            {

                foreach (BaseValidator current2 in (from validator in validatorCollection
                                                    where suppressedValidators.Any((ValidationSupression sv) => validator.ValidatorID.ToString() == database.GetItem(sv.ValidatorId).ID.ToString())
                                                    where suppressedValidators.Any((ValidationSupression sv) => sv.ItemId == validator.ItemUri.ItemID.ToString())
                                                    where suppressedValidators.Any((ValidationSupression sv) => sv.FieldId == validator.FieldID.ToString())
                                                    select validator).ToList<BaseValidator>())
                {
                    validatorCollection.Remove(current2);
                }
            }
            return validatorCollection;
        }
    }



    public class ValidationSupression
    {
        public string ItemId
        {
            get;
            set;
        }

        public string FieldId
        {
            get;
            set;
        }

        public string ValidatorId
        {
            get;
            set;
        }
    }
}

namespace Sitecore.Support.ExperienceEditor.Speak.Ribbon.Requests.FieldsValidation
{
    public class ValidateFields : PipelineProcessorRequest<Sitecore.ExperienceEditor.Speak.Server.Contexts.PageContext>
    {
        public override PipelineProcessorResponseValue ProcessRequest()
        {
            Item item = base.RequestContext.Item;
            Assert.IsNotNull(item, "Item is null");
            PipelineProcessorResponseValue result;
            using (new ClientDatabaseSwitcher(item.Database))
            {
                ValidatorCollection expr_3B = FieldValidators.GetFieldsValidators(ValidatorsMode.ValidatorBar, this.GetControlsToValidate().Keys, item.Database);
                ValidatorManager.Validate(expr_3B, new ValidatorOptions(true));
                List<FieldValidationError> list = new List<FieldValidationError>();
                ID id = null;
                foreach (BaseValidator baseValidator in expr_3B)
                {
                    if (!baseValidator.IsValid && !(baseValidator.FieldID == id))
                    {
                        if (Sitecore.ExperienceEditor.Utils.WebUtility.IsEditAllVersionsTicked())
                        {
                            Field field = item.Fields[baseValidator.FieldID];
                            if (!field.Shared && !field.Unversioned)
                            {
                                continue;
                            }
                        }
                        list.Add(new FieldValidationError
                        {
                            Text = baseValidator.Text,
                            Title = baseValidator.Name,
                            FieldId = baseValidator.FieldID.ToString(),
                            DataSourceId = baseValidator.ItemUri.ItemID.ToString(),
                            Errors = baseValidator.Errors,
                            Priority = (int)baseValidator.Result
                        });
                    }
                }
                result = new PipelineProcessorResponseValue
                {
                    Value = list
                };
            }
            return result;
        }

        public SafeDictionary<FieldDescriptor, string> GetControlsToValidate()
        {
            Item item = base.RequestContext.Item;
            Assert.IsNotNull(item, "The item is null.");
            IEnumerable<PageEditorField> arg_33_0 = Sitecore.ExperienceEditor.Utils.WebUtility.GetFields(item.Database, base.RequestContext.FieldValues);
            SafeDictionary<FieldDescriptor, string> safeDictionary = new SafeDictionary<FieldDescriptor, string>();
            foreach (PageEditorField current in arg_33_0)
            {
                using (new LanguageSwitcher(item.Language.Name))
                {
                    Item arg_A5_0 = (item.ID == current.ItemID) ? item : item.Database.GetItem(current.ItemID);
                    Field field = item.Fields[current.FieldID];
                    string value = Sitecore.ExperienceEditor.Utils.WebUtility.HandleFieldValue(current.Value, field.TypeKey);
                    FieldDescriptor key = new FieldDescriptor(arg_A5_0.Uri, field.ID, value, false);
                    string text = current.ControlId ?? string.Empty;
                    safeDictionary[key] = text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        RuntimeValidationValues.Current[text] = value;
                    }
                }
            }
            return safeDictionary;
        }
    }
}


namespace Sitecore.Support.Pipelines.GetPageEditorValidators
{
    public class GetFieldValidators : GetPageEditorValidatorsProcessor
    {
        public override void Process(GetPageEditorValidatorsArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            ValidatorCollection fieldsValidators = FieldValidators.GetFieldsValidators(args.Mode, args.Fields, args.Item.Database);
            this.AddValidators(args, fieldsValidators);
        }
    }
}


//changed code lays in "return FieldValidators.GetFieldsValidators(mode, this.Options.Fields, database);" in the BuildValidators method
namespace Sitecore.Support.Shell.Applications.ContentManager
{
    public class FieldEditorForm : Sitecore.Shell.Applications.ContentManager.BaseEditorForm
    {


        /// <summary>
        /// Gets or sets the body.
        /// </summary>
        /// <value>
        /// The body.
        /// </value>
        protected System.Web.UI.HtmlControls.HtmlGenericControl Body
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the browser title.
        /// </summary>
        /// <value>
        /// The browser title.
        /// </value>
        protected System.Web.UI.WebControls.PlaceHolder BrowserTitle
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the buttons.
        /// </summary>
        /// <value>
        /// The buttons.
        /// </value>
        protected System.Web.UI.WebControls.Literal Buttons
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the content editor.
        /// </summary>
        /// <value>
        /// The content editor.
        /// </value>
        protected Border ContentEditor
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the dialog icon.
        /// </summary>
        /// <value>
        /// The dialog icon.
        /// </value>
        protected ThemedImage DialogIcon
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the dialog text.
        /// </summary>
        /// <value>
        /// The dialog text.
        /// </value>
        protected Sitecore.Web.UI.HtmlControls.Literal DialogText
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the dialog title.
        /// </summary>
        /// <value>
        /// The dialog title.
        /// </value>
        protected Sitecore.Web.UI.HtmlControls.Literal DialogTitle
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the type of the document.
        /// </summary>
        /// <value>
        /// The type of the document.
        /// </value>
        protected System.Web.UI.WebControls.PlaceHolder DocumentType
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the field editor custom params.
        /// </summary>
        /// <value>The field editor custom params.</value>
        protected System.Web.UI.WebControls.Literal CustomParamsContainer
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the warning text.
        /// </summary>
        /// <value>The warning text.</value>
        protected Sitecore.Web.UI.HtmlControls.Literal warningText
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the warning row.
        /// </summary>
        /// <value>The warning row.</value>
        protected System.Web.UI.HtmlControls.HtmlGenericControl WarningRow
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the stylesheets.
        /// </summary>
        /// <value>
        /// The stylesheets.
        /// </value>
        protected System.Web.UI.WebControls.PlaceHolder Stylesheets
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the options.
        /// </summary>
        /// <value>
        /// The options.
        /// </value>
        protected Sitecore.Shell.Applications.ContentManager.FieldEditorOptions Options
        {
            get;
            set;
        }


        /// <summary>
        /// Indicates whether current request is issued due to the section toggling
        /// </summary>
        private bool SectionToggling
        {
            get;
            set;
        }

        /// <summary>
        /// Handles the message.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        public override void HandleMessage(Message message)
        {
            Assert.ArgumentNotNull(message, "message");
            if (message.Name == "item:save")
            {
                this.Save();
                return;
            }
            if (message.Name == "fieldeditor:returnvalues")
            {
                this.ReturnValues();
                return;
            }
            if (message.Name == "fieldeditor:cancel")
            {
                FieldEditorForm.Cancel();
            }
        }



        /// <summary>
        /// Raises the load event.
        /// </summary>
        /// <param name="e">
        /// The <see cref="T:System.EventArgs" /> instance containing the event data.
        /// </param>
        /// <remarks>
        /// This method notifies the server control that it should perform actions common to each HTTP
        /// request for the page it is associated with, such as setting up a database query. At this
        /// stage in the page lifecycle, server controls in the hierarchy are created and initialized,
        /// view state is restored, and form controls reflect client-side data. Use the IsPostBack
        /// property to determine whether the page is being loaded in response to a client postback,
        /// or if it is being loaded and accessed for the first time.
        /// </remarks>
        protected override void OnLoad(System.EventArgs e)
        {
            base.OnLoad(e);
            string formValue = WebUtil.GetFormValue("scSections");
            if (!string.IsNullOrEmpty(formValue))
            {
                FieldEditorForm.HandleSections(new UrlString(formValue));
            }
            this.Options = Sitecore.Shell.Applications.ContentManager.FieldEditorOptions.Parse(new UrlString(WebUtil.GetQueryString()));
            if (Context.ClientPage.IsEvent)
            {
                return;
            }
            FieldEditorParameters parameters = this.Options.Parameters;
            this.RenderCustomParameters(parameters);
            string text = parameters["warningtext"];
            if (!string.IsNullOrEmpty(text))
            {
                this.ShowWarningMessage(text);
            }
            this.SetDocumentType();
            System.Web.UI.AttributeCollection attributes;
            (attributes = this.Body.Attributes)["class"] = attributes["class"] + string.Format(" {0}", UIUtil.GetBrowserClassString());
            this.ValidatorsKey = string.Format("VK_{0}", ID.NewID.ToShortID());
            System.Web.UI.Control control = Context.ClientPage.FindControl("ContentEditorForm");
            Assert.IsNotNull(control, "Form \"ContentEditorForm\" not found.");
            control.Controls.Add(new System.Web.UI.LiteralControl(string.Format("<input type=\"hidden\" id=\"scValidatorsKey\" name=\"scValidatorsKey\" value=\"{0}\"/>", this.ValidatorsKey)));
            int num = Settings.Validators.AutomaticUpdate ? Settings.Validators.UpdateDelay : 0;
            control.Controls.Add(new System.Web.UI.LiteralControl(string.Format("<input type=\"hidden\" id=\"scValidatorsUpdateDelay\" name=\"scValidatorsUpdateDelay\" value=\"{0}\"/>", num)));
            string arg = (WebUtil.GetQueryString("mo") == "preview") ? "Shell" : string.Format("CE_{0}", ID.NewID.ToShortID());
            control.Controls.Add(new System.Web.UI.LiteralControl(string.Format("<input id=\"__FRAMENAME\" name=\"__FRAMENAME\" type=\"hidden\" value=\"{0}\"/>", arg)));
            string @string = StringUtil.GetString(new string[]
            {
                this.Options.DialogTitle,
                string.Format("{0} - Sitecore Content Editor", Client.Site.BrowserTitle)
            });
            this.BrowserTitle.Controls.Add(new System.Web.UI.LiteralControl("<title>{0}</title>".FormatWith(new object[]
            {
                @string
            })));
            Tag arg2 = new Tag("input")
            {
                Type = "button",
                Value = Translate.Text("OK"),
                Class = "scButton scButtonPrimary",
                Click = "javascript:return scForm.invoke('fieldeditor:returnvalues')"
            };
            Tag arg3 = new Tag("input")
            {
                Type = "button",
                Value = Translate.Text("Cancel"),
                Class = "scButton",
                Click = "javascript:return scForm.invoke('fieldeditor:cancel')"
            };
            this.Buttons.Text = string.Format("{0}{1}", arg2, arg3);
        }

        /// <summary>
        /// Raises the pre-rendered event.
        /// </summary>
        /// <param name="e">
        /// The <see cref="T:System.EventArgs" /> instance containing the event data.
        /// </param>
        protected virtual void OnPreRendered(System.EventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");
            this.UpdateEditor();
            Context.ClientPage.Modified = false;
        }

        /// <summary>
        /// Saves the size of the field.
        /// </summary>
        /// <param name="templateFieldId">The template field id.</param>
        /// <param name="value">The value.</param>
        protected void SaveFieldSize(string templateFieldId, string value)
        {
            int @int = MainUtil.GetInt(value, -1);
            if (@int == -1)
            {
                return;
            }
            Registry.SetInt("/Current_User/Content Editor/Field Size/" + templateFieldId, @int);
        }

        /// <summary>
        /// Validates the item.
        /// </summary>
        protected void ValidateItem()
        {
            Assert.IsTrue(UserOptions.ContentEditor.ShowValidatorBar, "Validator bar is switched off in Content Editor.");
            string formValue = WebUtil.GetFormValue("scValidatorsKey");
            Sitecore.Data.Validators.ValidatorCollection validators = ValidatorManager.GetValidators(ValidatorsMode.ValidatorBar, formValue);
            ValidatorManager.Validate(validators, new ValidatorOptions(false));
            string text = ValidatorBarFormatter.RenderValidationResult(validators);
            SheerResponse.Eval(string.Format("scContent.renderValidators({0},{1})", StringUtil.EscapeJavascriptString(text), Settings.Validators.UpdateFrequency));
        }

        /// <summary>
        /// Expands or collapses a section.
        /// </summary>
        /// <param name="sectionName">Name of the section.</param>
        /// <param name="collapsed">Indicates if the section is collapsed.</param>
        protected void ToggleSection(string sectionName, string collapsed)
        {
            Assert.ArgumentNotNull(sectionName, "sectionName");
            Assert.ArgumentNotNull(collapsed, "collapsed");
            this.Options = Sitecore.Shell.Applications.ContentManager.FieldEditorOptions.Parse(new UrlString(WebUtil.GetQueryString()));
            ClientPipelineArgs clientPipelineArgs = Context.ClientPage.CurrentPipelineArgs as ClientPipelineArgs;
            Assert.IsNotNull(clientPipelineArgs, typeof(ClientPipelineArgs));
            if (clientPipelineArgs.IsPostBack)
            {
                if (clientPipelineArgs.Result == "no")
                {
                    return;
                }
                clientPipelineArgs.IsPostBack = false;
            }
            else if (collapsed == "0")
            {
                Pair<ValidatorResult, Sitecore.Data.Validators.BaseValidator> pair = this.ValidateSection(sectionName);
                ValidatorResult part = pair.Part1;
                Sitecore.Data.Validators.BaseValidator part2 = pair.Part2;
                if (part2 != null && part2.IsEvaluating)
                {
                    SheerResponse.Alert(Translate.Text("The fields in this section are currently being validated.\n\nYou must wait for validation to complete before you can collapse this section."), new string[0]);
                    clientPipelineArgs.AbortPipeline();
                    return;
                }
                if (part == ValidatorResult.CriticalError)
                {
                    string text = Translate.Text("Some of the fields in this section contain critical errors.\n\nThe fields in this section will not be revalidated if you save the current item while this section is collapsed.\nAre you sure you want to collapse this section?");
                    if (MainUtil.GetBool(clientPipelineArgs.CustomData["showvalidationdetails"], false) && part2 != null)
                    {
                        text += ValidatorManager.GetValidationErrorDetails(part2);
                    }
                    SheerResponse.Confirm(text);
                    clientPipelineArgs.WaitForPostBack();
                    return;
                }
                if (part == ValidatorResult.FatalError)
                {
                    string text2 = Translate.Text("Some of the fields in this section contain fatal errors.\n\nYou must resolve these errors before you can collapse this section.");
                    if (MainUtil.GetBool(clientPipelineArgs.CustomData["showvalidationdetails"], false) && part2 != null)
                    {
                        text2 += ValidatorManager.GetValidationErrorDetails(part2);
                    }
                    SheerResponse.Alert(text2, new string[0]);
                    clientPipelineArgs.AbortPipeline();
                    return;
                }
            }
            if (collapsed == "0")
            {
                this.SaveFieldValuesInHandler(sectionName);
            }
            UrlString urlString = new UrlString(Registry.GetString("/Current_User/Content Editor/Sections/Collapsed"));
            urlString[sectionName] = ((collapsed == "1") ? "0" : "1");
            Registry.SetString("/Current_User/Content Editor/Sections/Collapsed", urlString.ToString());
            this.SectionToggling = true;
        }

        /// <summary>
        /// Save values inputed by user into handler that is avilably through hdl querystring parameter.
        /// </summary>
        /// <param name="sectionName">Name of section which values should be saved into handler.</param>
        protected void SaveFieldValuesInHandler(string sectionName)
        {
            Assert.ArgumentNotNull(sectionName, "sectionName");
            System.Collections.Generic.Dictionary<ID, FieldDescriptor> dictionary = this.Options.Fields.ToDictionary((FieldDescriptor p) => p.FieldID);
            System.Collections.Generic.Dictionary<ID, string> fieldValues = FieldEditorForm.GetFieldValues(base.FieldInfo);
            Editor.Section editorSection = this.GetEditorSection(sectionName);
            foreach (Editor.Field current in editorSection.Fields)
            {
                if (fieldValues.ContainsKey(current.ItemField.ID))
                {
                    dictionary[current.ItemField.ID].Value = fieldValues[current.ItemField.ID];
                }
            }
            UrlHandle urlHandle = this.Options.ToUrlHandle();
            urlHandle.Handle = new UrlString(WebUtil.GetQueryString())["hdl"];
            urlHandle.ToHandleString();
        }

        /// <summary>
        /// Validate specified section.
        /// </summary>
        /// <param name="sectionName">Name of the section to validate.</param>
        /// <returns>Worst validation result and failed validator.</returns>
        protected Pair<ValidatorResult, Sitecore.Data.Validators.BaseValidator> ValidateSection(string sectionName)
        {
            Assert.ArgumentNotNull(sectionName, "sectionName");
            string formValue = WebUtil.GetFormValue("scValidatorsKey");
            Sitecore.Data.Validators.ValidatorCollection validators = ValidatorManager.GetValidators(ValidatorsMode.ValidatorBar, formValue);
            Sitecore.Data.Validators.ValidatorCollection validatorCollection = new Sitecore.Data.Validators.ValidatorCollection();
            Editor.Section editorSection = this.GetEditorSection(sectionName);
            if (editorSection != null)
            {
                using (System.Collections.Generic.List<Editor.Field>.Enumerator enumerator = editorSection.Fields.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        Editor.Field field = enumerator.Current;
                        foreach (Sitecore.Data.Validators.BaseValidator current in from v in validators
                                                                                   where v.FieldID == field.ItemField.ID
                                                                                   select v)
                        {
                            validatorCollection.Add(current);
                        }
                    }
                }
            }
            ValidatorManager.Validate(validatorCollection, new ValidatorOptions(false));
            return ValidatorManager.GetStrongestResult(validators, true, true);
        }

        /// <summary>
        /// Returnes field editor section with specified name.
        /// </summary>
        /// <param name="sectionName">Name of section to return.</param>
        /// <returns>Field editor section with specified name.</returns>
        protected Editor.Section GetEditorSection(string sectionName)
        {
            Assert.ArgumentNotNull(sectionName, "sectionName");
            Sitecore.Shell.Applications.ContentManager.FieldEditor fieldEditor = new Sitecore.Shell.Applications.ContentManager.FieldEditor
            {
                PreserveSections = this.Options.PreserveSections
            };
            return fieldEditor.GetEditorSections(this.Options.Fields, base.FieldInfo)[sectionName];
        }

        /// <summary>
        /// Retrieves field values inputed by user.
        /// </summary>
        /// <param name="fieldInfo">Collection with fields info.</param>
        /// <returns>Dicionary with inputed field values.</returns>
        protected static System.Collections.Generic.Dictionary<ID, string> GetFieldValues(System.Collections.Hashtable fieldInfo)
        {
            Assert.ArgumentNotNull(fieldInfo, "fieldInfo");
            System.Collections.Generic.Dictionary<ID, string> dictionary = new System.Collections.Generic.Dictionary<ID, string>();
            foreach (Sitecore.Shell.Applications.ContentManager.FieldInfo fieldInfo2 in fieldInfo.Values)
            {
                System.Web.UI.Control control = Context.ClientPage.FindSubControl(fieldInfo2.ID);
                if (control != null)
                {
                    string text = (control is IContentField) ? StringUtil.GetString(new string[]
                    {
                        (control as IContentField).GetValue()
                    }) : StringUtil.GetString(ReflectionUtil.GetProperty(control, "Value"));
                    if (!(text == "__#!$No value$!#__"))
                    {
                        string a = fieldInfo2.Type.ToLowerInvariant();
                        if (a == "rich text" || a == "html")
                        {
                            text = text.TrimEnd(new char[]
                            {
                                ' '
                            });
                        }
                        dictionary[fieldInfo2.FieldID] = text;
                    }
                }
            }
            return dictionary;
        }

        /// <summary>
        /// Shows the warning message.
        /// </summary>
        /// <param name="warningText">The warning text.</param>
        private void ShowWarningMessage(string warningText)
        {
            Assert.ArgumentNotNull(warningText, "warningText");
            this.warningText.Text = warningText;
            this.WarningRow.Visible = true;
        }

        /// <summary>
        /// Renders the custom parameters.
        /// </summary>
        /// <param name="parameters">The parameters.</param>
        private void RenderCustomParameters(FieldEditorParameters parameters)
        {
            Assert.ArgumentNotNull(parameters, "parameters");
            this.CustomParamsContainer.Text = string.Format("<input type='hidden' id='{0}' name='{0}' value='{1}' />", "FieldEditorCustomParams", parameters.Serialize());
        }

        /// <summary>
        /// Gets the save packet.
        /// </summary>
        /// <param name="fieldInfo">
        ///   The field info.
        /// </param>
        /// <returns>
        /// The save packet.
        /// </returns>
        private static Packet GetSavePacket(System.Collections.Hashtable fieldInfo)
        {
            FieldEditorOptions fieldEditorOptions = FieldEditorOptions.Parse(new UrlString(WebUtil.GetQueryString()));
            System.Collections.Generic.ICollection<FieldDescriptor> fields = fieldEditorOptions.Fields;
            Assert.ArgumentNotNull(fieldInfo, "fieldInfo");
            System.Collections.Generic.Dictionary<ID, FieldDescriptor> dictionary = fields.ToDictionary((FieldDescriptor p) => p.FieldID);
            System.Collections.Generic.Dictionary<ID, string> fieldValues = FieldEditorForm.GetFieldValues(fieldInfo);
            Packet packet = new Packet();
            foreach (Sitecore.Shell.Applications.ContentManager.FieldInfo fieldInfo2 in fieldInfo.Values)
            {
                if (fieldValues.ContainsKey(fieldInfo2.FieldID))
                {
                    packet.StartElement("field");
                    packet.SetAttribute("itemuri", dictionary[fieldInfo2.FieldID].ItemUri.ToString());
                    packet.SetAttribute("itemid", fieldInfo2.ItemID.ToString());
                    packet.SetAttribute("language", fieldInfo2.Language.ToString());
                    packet.SetAttribute("version", fieldInfo2.Version.ToString());
                    packet.SetAttribute("fieldid", fieldInfo2.FieldID.ToString());
                    packet.AddElement("value", fieldValues[fieldInfo2.FieldID], new string[0]);
                    packet.EndElement();
                }
            }
            return Assert.ResultNotNull<Packet>(packet);
        }

        /// <summary>
        /// Handles the sections.
        /// </summary>
        /// <param name="sections">
        /// The sections.
        /// </param>
        private static void HandleSections(UrlString sections)
        {
            Assert.ArgumentNotNull(sections, "sections");
            UrlString urlString = new UrlString(Registry.GetString("/Current_User/Content Editor/Sections/Collapsed"));
            foreach (string text in sections.Parameters.Keys)
            {
                urlString[System.Web.HttpUtility.UrlDecode(text)] = sections[text];
            }
            Registry.SetString("/Current_User/Content Editor/Sections/Collapsed", urlString.ToString());
            SheerResponse.SetAttribute("scSections", "value", string.Empty);
        }

        /// <summary>
        /// Cancels this instance.
        /// </summary>
        private static void Cancel()
        {
            SheerResponse.SetModified(false);
            SheerResponse.CloseWindow();
        }

        /// <summary>
        /// Renders the editor.
        /// </summary>
        /// <param name="parent">
        /// The parent.
        /// </param>
        private void RenderEditor(Border parent)
        {
            Assert.ArgumentNotNull(parent, "parent");
            Assert.IsNotNull(this.Options, "Editor options");
            Sitecore.Shell.Applications.ContentManager.FieldEditor fieldEditor = new Sitecore.Shell.Applications.ContentManager.FieldEditor
            {
                DefaultIcon = this.Options.Icon,
                DefaultTitle = this.Options.Title,
                PreserveSections = this.Options.PreserveSections,
                ShowInputBoxes = this.Options.ShowInputBoxes,
                ShowSections = this.Options.ShowSections
            };
            if (!Context.ClientPage.IsEvent)
            {
                if (!string.IsNullOrEmpty(this.Options.Title))
                {
                    this.DialogTitle.Text = this.Options.Title;
                }
                if (!string.IsNullOrEmpty(this.Options.Text))
                {
                    this.DialogText.Text = this.Options.Text;
                }
                if (!string.IsNullOrEmpty(this.Options.Icon))
                {
                    this.DialogIcon.Src = this.Options.Icon;
                }
            }
            fieldEditor.Render(this.Options.Fields, base.FieldInfo, parent);
            if (Context.ClientPage.IsEvent)
            {
                SheerResponse.SetInnerHtml("ContentEditor", parent);
            }
        }

        /// <summary>
        /// Returns the values.
        /// </summary>
        private void ReturnValues()
        {
            Sitecore.Shell.Applications.ContentManager.FieldEditorOptions fieldEditorOptions = Sitecore.Shell.Applications.ContentManager.FieldEditorOptions.Parse(new UrlString(WebUtil.GetQueryString()));
            ReturnFieldEditorValuesArgs args = new ReturnFieldEditorValuesArgs(fieldEditorOptions, base.FieldInfo);
            using (new LongRunningOperationWatcher(1000, "uiReturnFieldEditorValues pipeline", new string[0]))
            {
                if (!fieldEditorOptions.SaveItem || this.Save())
                {
                    Context.ClientPage.Start("uiReturnFieldEditorValues", args);
                }
            }
        }

        /// <summary>
        /// Saves the specified message.
        /// </summary>
        /// <returns>
        /// The save result.
        /// </returns>
        public bool Save()
        {
            System.Collections.Hashtable fieldInfo = base.FieldInfo;
            Packet savePacket = FieldEditorForm.GetSavePacket(fieldInfo);
            SaveArgs saveArgs = new SaveArgs(savePacket.XmlDocument);
            Context.ClientPage.Start("saveUI", saveArgs);
            if (saveArgs.Error.Length > 0)
            {
                SheerResponse.Alert(saveArgs.Error, new string[0]);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sets the type of the document.
        /// </summary>
        private void SetDocumentType()
        {
            string text = "<!DOCTYPE html>";
            DeviceCapabilities capabilities = Client.Device.Capabilities;
            if (capabilities != null)
            {
                text = StringUtil.GetString(new string[]
                {
                    capabilities.GetDefaultDocumentType(),
                    text
                });
            }
            this.DocumentType.Controls.Add(new System.Web.UI.LiteralControl(text));
        }

        /// <summary>
        /// Updates the editor.
        /// </summary>
        private void UpdateEditor()
        {
            if (Context.ClientPage.IsEvent && !this.SectionToggling)
            {
                return;
            }
            if (this.SectionToggling)
            {
                base.FieldInfo.Clear();
            }
            Border border = new Border();
            this.ContentEditor.Controls.Clear();
            border.ID = "Editors";
            Context.ClientPage.AddControl(this.ContentEditor, border);
            this.RenderEditor(border);
            this.UpdateValidatorBar(border);
        }

        /// <summary>
        /// Updates the validator bar.
        /// </summary>
        /// <param name="parent">
        /// The parent.
        /// </param>
        private void UpdateValidatorBar(Border parent)
        {
            Assert.ArgumentNotNull(parent, "parent");
            if (!UserOptions.ContentEditor.ShowValidatorBar)
            {
                return;
            }
            Sitecore.Data.Validators.ValidatorCollection validatorCollection = this.BuildValidators(ValidatorsMode.ValidatorBar);
            ValidatorManager.Validate(validatorCollection, new ValidatorOptions(false));
            string text = ValidatorBarFormatter.RenderValidationResult(validatorCollection);
            bool flag = text.IndexOf("Applications/16x16/bullet_square_grey.png", System.StringComparison.InvariantCulture) >= 0;
            System.Web.UI.Control control = parent.FindControl("ValidatorPanel");
            if (control == null)
            {
                return;
            }
            control.Controls.Add(new System.Web.UI.LiteralControl(text));
            System.Web.UI.Control control2 = Context.ClientPage.FindControl("ContentEditorForm");
            control2.Controls.Add(new System.Web.UI.LiteralControl(string.Format("<input type=\"hidden\" id=\"scHasValidators\" name=\"scHasValidators\" value=\"{0}\"/>", (validatorCollection.Count > 0) ? "1" : string.Empty)));
            if (flag)
            {
                control.Controls.Add(new System.Web.UI.LiteralControl(string.Format("<script type=\"text/javascript\" language=\"javascript\">window.setTimeout('scContent.updateValidators()', {0})</script>", Settings.Validators.UpdateFrequency)));
            }
            control.Controls.Add(new System.Web.UI.LiteralControl("<script type=\"text/javascript\" language=\"javascript\">scContent.updateFieldMarkers()</script>"));
        }



        /// <summary>
        /// Builds the validators.
        /// </summary>
        /// <param name="mode">
        /// The editor mode.
        /// </param>
        /// <returns>
        /// The validators.
        /// </returns>
        protected Sitecore.Data.Validators.ValidatorCollection BuildValidators(ValidatorsMode mode)
        {
            if (this.Options.Fields.Count == 0)
            {
                return new Sitecore.Data.Validators.ValidatorCollection();
            }
            SafeDictionary<string, string> safeDictionary = new SafeDictionary<string, string>();
            foreach (Sitecore.Shell.Applications.ContentManager.FieldInfo fieldInfo in base.FieldInfo.Values)
            {
                safeDictionary[fieldInfo.FieldID.ToString()] = fieldInfo.ID;
            }
            Database database = Factory.GetDatabase(this.Options.Fields.First<FieldDescriptor>().ItemUri.DatabaseName);
            ValidatorCollection collectionWithCorrectSuppression = FieldValidators.GetFieldsValidators(mode, this.Options.Fields, database);

            /*  MethodInfo method1 = typeof(ValidatorManager).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Where(mi=>mi.Name=="BuildFieldValidators").Where(mi=>mi.GetParameters().Length==4).FirstOrDefault();
              method1.Invoke(null, new object[] { mode, collectionWithCorrectSuppression, this.Options.Fields, database });*/
            // MethodInfo method = typeFromHandle.GetMethod("ConvertToFields", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo method2 = typeof(DefaultValidatorManager).GetMethod("PostprocessValidatorsForContentEditor", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            method2.Invoke(FieldValidators.Instance.Value, new object[] { this.ValidatorsKey, safeDictionary, mode, collectionWithCorrectSuppression });


            return collectionWithCorrectSuppression;
            //  return ValidatorManager.BuildFieldValidators(mode, collectionWithCorrectSuppression, this.Options.Fields, database);
        }



        private static System.Collections.Generic.List<Field> ConvertToFields(System.Collections.Generic.IEnumerable<FieldDescriptor> fields)
        {
            System.Collections.Generic.List<Field> list = new System.Collections.Generic.List<Field>();
            foreach (FieldDescriptor current in fields)
            {
                Item item = Database.GetItem(current.ItemUri);
                if (item != null)
                {
                    Field field = item.Fields[current.FieldID];
                    if (field != null)
                    {
                        list.Add(field);
                    }
                }
            }
            return list;
        }

        private string ValidatorsKey
        {
            get
            {
                if (string.IsNullOrEmpty(this.validatorsKey))
                {
                    this.validatorsKey = WebUtil.GetFormValue("scValidatorsKey");
                }
                return this.validatorsKey;
            }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                this.validatorsKey = value;
            }
        }

        private string validatorsKey;
    }
}