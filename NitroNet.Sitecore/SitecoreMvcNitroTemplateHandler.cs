﻿using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using NitroNet.Mvc;
using NitroNet.Sitecore.DynamicPlaceholder;
using NitroNet.Sitecore.Rendering;
using NitroNet.ViewEngine;
using NitroNet.ViewEngine.TemplateHandler;
using Sitecore.Diagnostics;
using Sitecore.Mvc;
using Sitecore.Mvc.Presentation;
using RenderingContext = Veil.RenderingContext;
using SC = Sitecore;

namespace NitroNet.Sitecore
{
	public class SitecoreMvcNitroTemplateHandler : INitroTemplateHandler
	{
        private const string ThisIdentifier = "this";
	    private const string SkinParameter = "template";
	    private const string DataParameter = "data";
	    private const string ModelParameter = "model";

		private readonly IComponentRepository _componentRepository;
	    private readonly ISitecoreRenderingRepository _renderingRepository;

	    public SitecoreMvcNitroTemplateHandler(IComponentRepository componentRepository, ISitecoreRenderingRepository renderingRepository)
	    {
	        _componentRepository = componentRepository;
	        _renderingRepository = renderingRepository;
	    }

	    private static HtmlHelper CreateHtmlHelper(RenderingContext context)
		{
			return CreateHtmlHelper(GetMvcContext(context));
		}

		private static MvcRenderingContext GetMvcContext(RenderingContext context)
		{
			var mvcContext = context as MvcRenderingContext;
			if (mvcContext == null)
				throw new InvalidOperationException("SitecoreMvcNitroTemplateHandler can only be used inside a Mvc application.");
			return mvcContext;
		}

		private static HtmlHelper CreateHtmlHelper(MvcRenderingContext mvcContext)
		{
			return new HtmlHelper(mvcContext.ViewContext, mvcContext.ViewDataContainer);
		}

		public Task RenderPlaceholderAsync(object model, string key, string index, RenderingContext context)
		{
			throw new NotImplementedException();
		}

		public void RenderPlaceholder(object model, string key, string index, RenderingContext context)
		{
			var htmlHelper = CreateHtmlHelper(context);
		    var dynamicKey = key;
		    if (!string.IsNullOrEmpty(index))
		    {
		        dynamicKey = key + "_" + index;
		    }
				

			context.Writer.Write(htmlHelper.Sitecore().DynamicPlaceholder(dynamicKey));
		}

	    public void RenderComponent(RenderingParameter component, RenderingParameter skin, RenderingParameter dataVariation,
	        object model, RenderingContext context)
	    {
            var requestContext = PageContext.Current.RequestContext;
	        var savedSkin = requestContext.RouteData.Values[SkinParameter];
	        var savedModel = requestContext.RouteData.Values[ModelParameter];
	        var savedDataVariation = requestContext.RouteData.Values[DataParameter];
	        try
	        {
                // Try to get values from model
                AggregateRenderingParameter(component, model);
                AggregateRenderingParameter(skin, model);

                if (string.IsNullOrEmpty(dataVariation.Value))
	            {
	                dataVariation.Value = component.Value;
	            }
	            
	            var propertyName = CleanName(dataVariation.Value);

	            object subModel = null;

	            if (dataVariation.Value.Equals(ThisIdentifier))
	            {
	                subModel = model;
	            }

	            var modelFound = false;

	            if (subModel == null)
	            {
	                modelFound = GetValueFromObjectHierarchically(model, propertyName, out subModel);
	            }

	            if (subModel != null && !(subModel is string))
	            {
                    var componentIdBySkin = GetComponentId(component.Value, skin.Value);
                    RenderPartial(componentIdBySkin, subModel, context);
	                return;
	            }

	            if (modelFound && subModel == null)
	            {
	                Log.Error(
                        string.Format("Property {0} of model {1} is null.", propertyName, model.GetType()), this);
	                return;
	            }

                var htmlHelper = CreateHtmlHelper(context);
                var parts = component.Value.Split('/');
                var componentName = parts[parts.Length - 1];
                var cleanComponentName = CleanName(componentName);
                var renderingId = _renderingRepository.GetRenderingId(cleanComponentName);
                requestContext.RouteData.Values[SkinParameter] = skin.Value ?? string.Empty;
	            requestContext.RouteData.Values[DataParameter] = dataVariation.Value ?? string.Empty;

	            if (renderingId != null)
	            {
	                // TODO: Cache!
	                context.Writer.Write(htmlHelper.Sitecore()
	                    .Rendering(renderingId, new  { data = dataVariation.Value ?? string.Empty}));
	            }
	            else
	            {
                    var controller = CleanControllerName(componentName);

                    var output = new HtmlString(string.Empty);

	                try
	                {
	                    output = htmlHelper.Sitecore().Controller(controller);
                    }
	                catch (ControllerCreationException)
	                {
                        Log.Warn(string.Format("No controller found for '{0}'. The component is not rendered.", controller), this);

#if DEBUG
                        output = new HtmlString(string.Format("<!-- Missing controller: '{0}' -->", controller));
#endif
                    }
                    context.Writer.Write(output);

                    Log.Warn(
                        string.Format(
                            "Controller {0} gets directly called by NitroNet. " +
                            "Consider to create a rendering with name \"{1}\" in order to let the controller be called by the Sitecore rendering pipeline. " +
                            "Component: {2}, Template: {3}, Data: {4}",
                            controller, cleanComponentName, component.Value, skin.Value, dataVariation.Value), this);
                }
	        }
	        finally
	        {
	            requestContext.RouteData.Values[SkinParameter] = savedSkin;
	            requestContext.RouteData.Values[DataParameter] = savedDataVariation;
	            requestContext.RouteData.Values[ModelParameter] = savedModel;
	        }
	    }

		public Task RenderLabelAsync(string key, RenderingContext context)
		{
            var label = SC.Globalization.Translate.Text(key);

            context.Writer.Write(label);

			return Task.FromResult(true);
		}

		public void RenderLabel(string key, RenderingContext context)
		{
		    var label = SC.Globalization.Translate.Text(key);

			context.Writer.Write(label);
		}

		public Task RenderPartialAsync(string template, object model, RenderingContext context)
		{
			throw new NotImplementedException();
		}

		public void RenderPartial(string template, object model, RenderingContext context)
		{
			CreateHtmlHelper(context).RenderPartial(template, model);
		}

        private string GetComponentId(string componentId, string skin)
        {
            var componentDefinition = _componentRepository.GetComponentDefinitionByIdAsync(componentId).Result;
            if (componentDefinition != null)
            {
                FileTemplateInfo templateInfo;
                if (string.IsNullOrEmpty(skin) || componentDefinition.Skins == null ||
                    !componentDefinition.Skins.TryGetValue(skin, out templateInfo))
                    templateInfo = componentDefinition.DefaultTemplate;

                return templateInfo.Id;
            }

            return null;
        }

        private static string CleanControllerName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var words = text.Split(' ', '-');
            var sb = new StringBuilder();

            foreach (var s in words)
            {
                var firstLetter = s.Substring(0, 1);
                var rest = s.Substring(1, s.Length - 1);
                sb.Append(firstLetter.ToUpper(CultureInfo.InvariantCulture) + rest);
            }

            return sb.ToString();
        }

        private string CleanName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace(" ", string.Empty).Replace("-", string.Empty).ToLower(CultureInfo.InvariantCulture);
        }

        private bool GetValueFromObjectHierarchically(object model, string propertyName, out object modelValue)
        {
            modelValue = null;
            if (propertyName.IndexOf(".", StringComparison.Ordinal) <= 0)
            {
                return GetValueFromObject(model, propertyName, out modelValue);
            }

            var subModel = model;
            foreach (var s in propertyName.Split('.'))
            {
                var modelFound = GetValueFromObject(subModel, s, out subModel);
                if (!modelFound)
                {
                    return false;
                }

                if (subModel == null)
                {
                    break;
                }
            }

            modelValue = subModel;
            return true;
	    }

	    private bool GetValueFromObject(object model, string propertyName, out object modelValue)
	    {
	        modelValue = null;
	        var dataProperty =
	            model.GetType().GetProperties().FirstOrDefault(prop => prop.Name.ToLower().Equals(propertyName));
	        if (dataProperty == null)
	        {
	            return false;
	        }

	        modelValue = dataProperty.GetValue(model);
	        return true;
	    }

	    private bool AggregateRenderingParameter(RenderingParameter renderingParameter, object model)
	    {
	        if (renderingParameter == null)
	        {
	            return false;
	        }

	        if (!renderingParameter.IsDynamic)
	        {
	            return false;
	        }

            var propertyName = CleanName(renderingParameter.Value);
            object dynamicName;
            if (GetValueFromObjectHierarchically(model, propertyName, out dynamicName) && dynamicName is string)
            {
                renderingParameter.Value = dynamicName.ToString();
                return true;
            }

	        return false;
	    }
	}
}
