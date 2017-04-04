﻿using Sitecore.Collections;
using Sitecore.Data.Validators;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Web;
using Sitecore.Web.UI.Sheer;
using System;
using Sitecore.Data.Items;
using Sitecore.Data.Fields;
using Sitecore.Data;
using System.Collections.Generic;
using Sitecore.Configuration;
using System.Linq;
using Sitecore.Pipelines.Save;
using Sitecore.Text;

namespace Sitecore.Support.Pipelines.Save
{
    /// <summary>
    /// Validates the fields.
    /// </summary>
    public class Validators
    {
        /// <summary>
        /// Runs the processor.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public void Process(SaveArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            this.ProcessInternal(args);

        }

        /// <summary>
        /// Processes the internal.
        /// </summary>
        /// <param name="args">
        /// The arguments.
        /// </param>
        protected void ProcessInternal(ClientPipelineArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            if (args.IsPostBack)
            {
                if (args.Result == "no")
                {
                    args.AbortPipeline();
                }
                args.IsPostBack = false;
                return;
            }
            string formValue = WebUtil.GetFormValue("scValidatorsKey");
            if (string.IsNullOrEmpty(formValue))
            {
                return;
            }

            Sitecore.Pipelines.Save.SaveArgs pipArgs = args as Sitecore.Pipelines.Save.SaveArgs;
            if (pipArgs == null)
            {
                Log.Error("Sitecore.Support.139071 : Error, pipArgs == null", this);
                return;
            }
            if (pipArgs.Items == null)
            {
                Log.Error("Sitecore.Support.139071 : Error, pipArgs.Items == null", this);
                return;
            }
            //Get All Validators
            ValidatorCollection allValidators = ValidatorManager.GetValidators(ValidatorsMode.ValidatorBar, formValue);
            if (allValidators == null)
            {
                Log.Error("Sitecore.Support.139071 : Error, allValidators == null", this);
                return;
            }
            Database db = Database.GetDatabase("master");
            if (db == null)
            {
                Log.Error("Sitecore.Support.139071 : Error, masterDb == null", this);
                return;
            }
            Item currItem = db.GetItem(pipArgs.Items[0].ID);
            if (currItem == null)
            {
                currItem = Sitecore.Context.Database.GetItem(pipArgs.Items[0].ID);
                if (currItem == null)
                {
                    Log.Error("Sitecore.Support.139071 : Error, can't get currItem", this);
                    return;
                }
            }
            //List with the Suppressed validators IDs
            List<ID> supValidatorItems = new List<ID>();

            //Final Validators collection
            ValidatorCollection validators = new ValidatorCollection();

            if (currItem.Fields["__Suppressed validation rules"] == null)
            {
                return;
            }
            if (currItem.Database == null)
            {
                Log.Error("Sitecore.Support.139071 : Error, currItem.Database = null", this);
                return;
            }
            if (!String.IsNullOrEmpty(currItem.Fields["__Suppressed validation rules"].Value))
            {
                string[] supressedValidations = currItem.Fields["__Suppressed validation rules"].Value.Split('|');
                if (supressedValidations == null)
                {
                    Log.Error("Sitecore.Support.139071 : Error, can't get supressedValidations rules", this);
                    return;
                }
                if (Database.GetDatabase("core") == null)
                {
                    Log.Error("Sitecore.Support.139071 : Error, can't get Core Db", this);
                    return;
                }
                if (supressedValidations.Length > 1)
                {
                    foreach (string str in supressedValidations)
                    {
                        try
                        {
                            supValidatorItems.Add(Database.GetDatabase("core").GetItem(str).ID ?? currItem.Database.GetItem(str).ID);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Sitecore.Support.139071 : Error, "+ ex.Message, this);
                            return;
                        }
                    }
                }
                else
                {
                    try
                    {
                        supValidatorItems.Add(Database.GetDatabase("core").GetItem(supressedValidations[0]).ID ?? currItem.Database.GetItem(supressedValidations[0]).ID);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Sitecore.Support.139071 : Error, " + ex.Message, this);
                        return;
                    }
                }


                foreach (BaseValidator val in allValidators)
                {
                    if (supValidatorItems.Contains(val.ValidatorID))
                    {
                        continue;
                    }
                    validators.Add(val);
                }
            }
            else
            {
                validators = allValidators;
            }

            ValidatorOptions options = new ValidatorOptions(true);
            ValidatorManager.Validate(validators, options);
            Pair<ValidatorResult, BaseValidator> strongestResult = ValidatorManager.GetStrongestResult(validators, true, true);
            ValidatorResult part = strongestResult.Part1;
            BaseValidator part2 = strongestResult.Part2;
            if (part2 != null && part2.IsEvaluating)
            {
                SheerResponse.Alert("The fields in this item have not been validated.\n\nWait until validation has been completed and then save your changes.", new string[0]);
                args.AbortPipeline();
                return;
            }
            if (part == ValidatorResult.CriticalError)
            {
                string text = Translate.Text("Some of the fields in this item contain critical errors.\n\nAre you sure you want to save this item?");
                if (MainUtil.GetBool(args.CustomData["showvalidationdetails"], false) && part2 != null)
                {
                    text += ValidatorManager.GetValidationErrorDetails(part2);
                }
                SheerResponse.Confirm(text);
                args.WaitForPostBack();
                return;
            }
            if (part == ValidatorResult.FatalError)
            {
                string text2 = Translate.Text("Some of the fields in this item contain fatal errors.\n\nYou must resolve these errors before you can save this item.");
                if (MainUtil.GetBool(args.CustomData["showvalidationdetails"], false) && part2 != null)
                {
                    text2 += ValidatorManager.GetValidationErrorDetails(part2);
                }
                SheerResponse.Alert(text2, new string[0]);
                SheerResponse.SetReturnValue("failed");
                args.AbortPipeline();
            }
        }
    }
}
