using System;
using System.Data;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using System.Web.UI.WebControls.WebParts;
using System.Collections;
using System.Collections.Generic;
using DotNetNuke.Common.Utilities;

using Ventrian.PropertyAgent;
using DotNetNuke.Entities.Modules;
using Satrabel.HttpModules.Provider;
using DotNetNuke.Framework.Providers;
using System.Text.RegularExpressions;

namespace Satrabel.OpenUrlRewriter.PropertyAgent
{
    public class PropertyAgentUrlRuleProvider : UrlRuleProvider
    {

        private const string ProviderType = "urlRule";
        private const string ProviderName = "propertyAgentUrlRuleProvider";

        private readonly ProviderConfiguration _providerConfiguration = ProviderConfiguration.GetProviderConfiguration(ProviderType);
        private readonly bool includePageName = true;
        
        public PropertyAgentUrlRuleProvider()
        {
            var objProvider = (DotNetNuke.Framework.Providers.Provider)_providerConfiguration.Providers[ProviderName];
            if (!String.IsNullOrEmpty(objProvider.Attributes["includePageName"]))
            {
                includePageName = bool.Parse(objProvider.Attributes["includePageName"]);
            }
            CacheKeys = new string[] { "PropertyAgent-ProperyValues-All" };
           
            HelpUrl = "https://openurlrewriter.codeplex.com/wikipage?title=PropertyAgent";
        }

        public override List<UrlRule> GetRules(int PortalId)
        {            
            List<UrlRule> Rules = new List<UrlRule>();
            PropertyController pc = new PropertyController();
            PropertyTypeController ptc = new PropertyTypeController();
            ModuleController mc = new ModuleController();
            ArrayList modules = mc.GetModulesByDefinition(PortalId, "Property Agent");
            //foreach (ModuleInfo module in modules.OfType<ModuleInfo>().GroupBy(m => m.ModuleID).Select(g => g.First())){                
            foreach (ModuleInfo module in modules.OfType<ModuleInfo>())
            {
                PropertySettings settings = new PropertySettings(module.ModuleSettings);
                if (settings.SEOAgentType.ToLower() == "agenttype" /* && settings.SEOViewPropertyTitle == "" */)
                {
                    Rules.AddRange(GetTypesRules(-1, ptc, module, settings, ""));
                    var properties = pc.List(module.ModuleID, Null.NullInteger, SearchStatusType.Any, Null.NullInteger, Null.NullInteger, false, SortByType.Published, Null.NullInteger, SortDirectionType.Ascending, "", "", 0, 9999, true);
                    foreach (PropertyInfo prop in properties)
                    {
                        var TypeRule = Rules.Single(r => r.Action == UrlRuleAction.Rewrite && r.Parameters == (settings.SEOAgentType + "=ViewType&" + settings.SEOPropertyTypeID + "=" + prop.PropertyTypeID.ToString()));

                        CustomFieldController cfc = new CustomFieldController();
                        PropertyValueController pvc = new PropertyValueController();
                        List<CustomFieldInfo> fields = cfc.List(module.ModuleID, false).OrderBy(c => c.SortOrder).ToList();

                        string url = "";
                        if (!string.IsNullOrEmpty(settings.SEOViewPropertyTitle))
                        {
                            url = TokenReplace(settings.SEOViewPropertyTitle, fields, prop.PropertyID, module.ModuleID, pvc);
                        }
                        else
                        {
                            CustomFieldInfo cfi = fields.First();
                            if (cfi != null)
                            {
                                PropertyValueInfo pvi = pvc.GetByCustomField(prop.PropertyID, cfi.CustomFieldID, module.ModuleID);
                                if (pvi != null)
                                {
                                    url = pvi.CustomValue;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(url))
                        {
                            var rule = new UrlRule
                            {
                                CultureCode = module.CultureCode,
                                TabId = module.TabID,
                                RuleType = UrlRuleType.Module,
                                Parameters = settings.SEOAgentType + "=View&" + settings.SEOPropertyID + "=" + prop.PropertyID.ToString(),
                                Action = UrlRuleAction.Rewrite,
                                Url = (settings.HideTypes ? "" : TypeRule.Url + "/") + CleanupUrl(url),
                                RemoveTab = !includePageName
                            };
                            if (Rules.Any(r => r.Url == rule.Url))
                            {
                                rule.Url = (settings.HideTypes ? "" : TypeRule.Url + "/") + prop.PropertyID.ToString() +"-"+ CleanupUrl(url);
                            }

                            Rules.Add(rule);
                            var ruleRedirect = new UrlRule
                            {
                                CultureCode = rule.CultureCode,
                                TabId = rule.TabId,
                                RuleType = rule.RuleType,
                                //Parameters = settings.SEOAgentType + "=View&" + settings.SEOPropertyID + "=" + prop.PropertyID.ToString(),
                                Action = UrlRuleAction.Redirect,
                                Url = rule.Parameters.Replace('=', '/').Replace('&', '/'),
                                RedirectDestination = rule.Url,
                                RemoveTab = rule.RemoveTab
                            };
                            if (rule.Url != ruleRedirect.Url)
                            {
                                //Rules.Add(ruleRedirect);
                            }
                        }
                    }
                    
                }
            }
            return Rules;
        }

        private List<UrlRule> GetTypesRules(int parentType, PropertyTypeController ptc, ModuleInfo module, PropertySettings settings, string parentUrl)
        {
            List<UrlRule> Rules = new List<UrlRule>();
            var propertyTypes = ptc.List(module.ModuleID, false, PropertyTypeSortByType.Standard, null, parentType);
            foreach (PropertyTypeInfo propType in propertyTypes)
            {
                var rule = new UrlRule
                {
                    CultureCode = module.CultureCode,
                    TabId = module.TabID,
                    RuleType = UrlRuleType.Module,
                    Parameters = settings.SEOAgentType + "=ViewType&" + settings.SEOPropertyTypeID + "=" + propType.PropertyTypeID.ToString(),
                    Action = UrlRuleAction.Rewrite,
                    Url = parentUrl+CleanupUrl(propType.Name),
                    RemoveTab = !includePageName
                };
                Rules.Add(rule);
                var ruleRedirect = new UrlRule
                {
                    CultureCode = rule.CultureCode,
                    TabId = rule.TabId,
                    RuleType = rule.RuleType,
                    Action = UrlRuleAction.Redirect,
                    Url = rule.Parameters.Replace('=', '/').Replace('&', '/'),
                    RedirectDestination = rule.Url,
                    RemoveTab = rule.RemoveTab
                };
                if (rule.Url != ruleRedirect.Url)
                {
                    //Rules.Add(ruleRedirect);
                }
                Rules.AddRange(GetTypesRules(propType.PropertyTypeID, ptc, module, settings, rule.Url+"/"));
            }
            return Rules;
        }

        private string TokenReplace(string template, List<CustomFieldInfo> replacements, int PropertyID, int ModuleID, PropertyValueController pvc)
        {
            var rex = new Regex(@"\[custom:([^\]]+)\]", RegexOptions.IgnoreCase);
            return (rex.Replace(template, delegate(Match m)
            {
                string key = m.Groups[1].Value;
                var cfi = replacements.SingleOrDefault(c => c.Name == key);
                if (cfi != null)
                {
                    PropertyValueInfo pvi = pvc.GetByCustomField(PropertyID, cfi.CustomFieldID, ModuleID);
                    if (pvi != null)
                    {
                        return pvi.CustomValue;
                    }
                }
                return m.Value;
            }));
        }
    }
}