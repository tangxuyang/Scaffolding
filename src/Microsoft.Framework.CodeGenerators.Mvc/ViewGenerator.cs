﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Framework.CodeGeneration;
using Microsoft.Framework.CodeGeneration.CommandLine;
using Microsoft.Framework.CodeGeneration.EntityFramework;
using Microsoft.Framework.Runtime;

namespace Microsoft.Framework.CodeGenerators.Mvc
{
    [Alias("view")]
    public class ViewGenerator : ICodeGenerator
    {
        private readonly IModelTypesLocator _modelTypesLocator;
        private readonly IEntityFrameworkService _entityFrameworkService;
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationEnvironment _applicationEnvironment;
        private readonly ICodeGeneratorActionsService _codeGeneratorActionsService;
        private readonly ILogger _logger;

        // Todo: Instead of each generator taking services, provide them in some base class?
        // However for it to be effective, it should be property dependecy injection rather
        // than constructor injection.
        public ViewGenerator(
            [NotNull]ILibraryManager libraryManager,
            [NotNull]IApplicationEnvironment environment,
            [NotNull]IModelTypesLocator modelTypesLocator,
            [NotNull]IEntityFrameworkService entityFrameworkService,
            [NotNull]ICodeGeneratorActionsService codeGeneratorActionsService,
            [NotNull]ILogger logger)
        {
            _libraryManager = libraryManager;
            _applicationEnvironment = environment;
            _codeGeneratorActionsService = codeGeneratorActionsService;
            _modelTypesLocator = modelTypesLocator;
            _entityFrameworkService = entityFrameworkService;
            _logger = logger;
        }

        public virtual IEnumerable<string> TemplateFolders
        {
            get
            {
                return TemplateFoldersUtilities.GetTemplateFolders(
                    containingProject: Constants.ThisAssemblyName,
                    baseFolders: new[] { typeof(ViewGenerator).Name },
                    applicationBasePath: _applicationEnvironment.ApplicationBasePath,
                    libraryManager: _libraryManager);
            }
        }

        public async Task GenerateCode([NotNull]ViewGeneratorModel viewGeneratorModel)
        {
            // Validate model
            string validationMessage;
            ITypeSymbol model, dataContext;

            if (!ValidationUtil.TryValidateType(viewGeneratorModel.ModelClass, "model", _modelTypesLocator, out model, out validationMessage))
            {
                throw new ArgumentException(validationMessage);
            }

            if (string.IsNullOrEmpty(viewGeneratorModel.ViewName))
            {
                throw new ArgumentException("The ViewName cannot be empty");
            }

            if (viewGeneratorModel.ViewName.EndsWith(Constants.ViewExtension, StringComparison.OrdinalIgnoreCase))
            {
                int viewNameLength = viewGeneratorModel.ViewName.Length - Constants.ViewExtension.Length;
                viewGeneratorModel.ViewName = viewGeneratorModel.ViewName.Substring(0, viewNameLength);
            }

            if (string.IsNullOrEmpty(viewGeneratorModel.TemplateName))
            {
                throw new ArgumentException("The TemplateName cannot be empty");
            }

            ValidationUtil.TryValidateType(viewGeneratorModel.DataContextClass, "dataContext", _modelTypesLocator, out dataContext, out validationMessage);

            // Validation successful
            Contract.Assert(model != null, "Validation succeded but model type not set");

            var appbasePath = _applicationEnvironment.ApplicationBasePath;
            var outputPath = Path.Combine(
                appbasePath,
                Constants.ViewsFolderName,
                model.Name,
                viewGeneratorModel.ViewName + Constants.ViewExtension);

            if (File.Exists(outputPath) && !viewGeneratorModel.Force)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    "View file {0} exists, use -f option to overwrite",
                    outputPath));
            }

            var templateName = viewGeneratorModel.TemplateName + Constants.RazorTemplateExtension;

            var dbContextFullName = dataContext != null ? dataContext.FullNameForSymbol() : viewGeneratorModel.DataContextClass;
            var modelTypeFullName = model.FullNameForSymbol();

            var modelMetadata = await _entityFrameworkService.GetModelMetadata(
                dbContextFullName,
                model);

            bool isLayoutSelected = viewGeneratorModel.UseDefaultLayout ||
                !String.IsNullOrEmpty(viewGeneratorModel.LayoutPage);

            var templateModel = new ViewGeneratorTemplateModel()
            {
                ViewDataTypeName = modelTypeFullName,
                ViewDataTypeShortName = model.Name,
                ViewName = viewGeneratorModel.ViewName,
                LayoutPageFile = viewGeneratorModel.LayoutPage,
                IsLayoutPageSelected = isLayoutSelected,
                IsPartialView = viewGeneratorModel.PartialView,
                ReferenceScriptLibraries = viewGeneratorModel.ReferenceScriptLibraries,
                ModelMetadata = modelMetadata,
                JQueryVersion = "1.10.2" //Todo
            };

            await _codeGeneratorActionsService.AddFileFromTemplateAsync(outputPath, templateName, TemplateFolders, templateModel);
            _logger.LogMessage("Added View : " + outputPath.Substring(appbasePath.Length));
        }
    }
}