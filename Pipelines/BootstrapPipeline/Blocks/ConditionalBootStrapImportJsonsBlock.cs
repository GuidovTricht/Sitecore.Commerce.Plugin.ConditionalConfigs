using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Core.Commands;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using Sitecore.Commerce.Plugin.ConditionalConfigs.Entities;
using SearchOption = System.IO.SearchOption;

namespace Sitecore.Commerce.Plugin.ConditionalConfigs.Pipelines.BootstrapPipeline.Blocks
{
    public class ConditionalBootStrapImportJsonsBlock : PipelineBlock<string, string, CommercePipelineExecutionContext>
    {
        private readonly NodeContext _nodeContext;
        private readonly ImportEnvironmentCommand _importEnvironmentCommand;
        private readonly ImportPolicySetCommand _importPolicySetCommand;
        private readonly IConfiguration _configuration;

        public ConditionalBootStrapImportJsonsBlock(NodeContext nodeContext, ImportEnvironmentCommand importEnvironmentCommand, ImportPolicySetCommand importPolicySetCommand, IConfiguration configuration)
        {
            _nodeContext = nodeContext;
            _importEnvironmentCommand = importEnvironmentCommand;
            _importPolicySetCommand = importPolicySetCommand;
            _configuration = configuration;
        }

        public override async Task<string> Run(string arg, CommercePipelineExecutionContext context)
        {
            Condition.Requires<string>(arg).IsNotNull<string>($"{this.Name}: The argument cannot be null.");
            var files = Directory.GetFiles(_nodeContext.WebRootPath + "\\data\\environments", "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var content = File.ReadAllText(file);
                var jobject = JObject.Parse(content);
                if (jobject.HasValues)
                {
                    var properties = jobject.Properties();
                    Func<JProperty, bool> typePredicate = (Func<JProperty, bool>)(p => p.Name.Equals("$type", StringComparison.OrdinalIgnoreCase));
                    if (properties.Any<JProperty>(typePredicate))
                    {
                        var type = jobject.Properties().FirstOrDefault<JProperty>(typePredicate);
                        if (string.IsNullOrEmpty(type?.Value?.ToString()))
                        {
                            context.Logger.LogError($"{this.Name}.Invalid type in json file '{file}'.");
                            break;
                        }

                        if (type.Value.ToString().Contains(typeof(CommerceEnvironment).FullName))
                        {
                            await HandleCommerceEnvironmentConfig(file, content, context);
                        }
                        else if (type.Value.ToString().Contains(typeof(PolicySet).FullName))
                        {
                            await HandlePolicySetConfig(file, content, context);
                        }
                        else if (type.Value.ToString().Contains(typeof(ConditionalPolicySet).FullName))
                        {
                            await HandleConditionalPolicySetConfig(file, content, jobject, context);
                        }
                        continue;
                    }
                }
                context.Logger.LogError($"{this.Name}.Invalid json file '{file}'.");
                break;
            }

            return arg;
        }

        private bool ConditionsMatch(IDictionary<string, string> conditions, CommercePipelineExecutionContext context)
        {
            foreach (var condition in conditions.Where(c => !string.IsNullOrEmpty(c.Key) && !string.IsNullOrEmpty(c.Value)))
            {
                var configurationValue = _configuration.GetSection($"AppSettings:{condition.Key}")?.Value;
                if (string.IsNullOrEmpty(configurationValue))
                {
                    context.Logger.LogWarning($"{this.Name}.ConditionsMatch: AppSetting not found for '{condition.Key}'.");
                    return false;
                }
                var regex = new Regex(condition.Value);
                if (!regex.IsMatch(configurationValue))
                {
                    context.Logger.LogInformation($"{this.Name}.ConditionsMatch: Condition did not match for setting '{condition.Key}' with condition '{condition.Value}'.");
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> HandleCommerceEnvironmentConfig(string file, string content, CommercePipelineExecutionContext context)
        {
            context.Logger.LogInformation($"{this.Name}.ImportEnvironmentFromFile: File={file}");
            try
            {
                var commerceEnvironment =
                    await _importEnvironmentCommand.Process(context.CommerceContext, content);
                context.Logger.LogInformation($"{this.Name}.EnvironmentImported: EnvironmentId={commerceEnvironment.Id}|File={file}");
                return true;
            }
            catch (Exception ex)
            {
                context.CommerceContext.LogException($"{this.Name}.ImportEnvironmentFromFile", ex);
            }

            return false;
        }

        private async Task<bool> HandlePolicySetConfig(string file, string content, CommercePipelineExecutionContext context)
        {
            context.Logger.LogInformation($"{this.Name}.ImportPolicySetFromFile: File={file}");
            try
            {
                var policySet = await _importPolicySetCommand.Process(context.CommerceContext, content);
                context.Logger.LogInformation($"{this.Name}.PolicySetImported: PolicySetId={policySet.Id}|File={file}");
                return true;
            }
            catch (Exception ex)
            {
                context.CommerceContext.LogException($"{this.Name}.ImportPolicySetFromFile", ex);
            }

            return false;
        }

        private async Task<bool> HandleConditionalPolicySetConfig(string file, string content, JObject jobject,
            CommercePipelineExecutionContext context)
        {
            context.Logger.LogInformation($"{this.Name}.ImportConditionalPolicySetFromFile: File={file}");
            try
            {
                Func<JProperty, bool> conditionsPredicate = (Func<JProperty, bool>)(p => p.Name.Equals("Conditions", StringComparison.OrdinalIgnoreCase));
                if (jobject.Properties().Any<JProperty>(conditionsPredicate))
                {
                    var conditions = jobject.Properties().FirstOrDefault<JProperty>(conditionsPredicate)?.Value?.ToObject<IDictionary<string, string>>();
                    if (conditions != null)
                    {
                        if (ConditionsMatch(conditions, context))
                        {
                            var policySet = await _importPolicySetCommand.Process(context.CommerceContext, content);
                            context.Logger.LogInformation($"{this.Name}.ConditionalPolicySetImported: PolicySetId={policySet.Id}|File={file}");
                            return true;
                        }
                        else
                        {
                            context.Logger.LogInformation($"{this.Name}.ConditionsDidNotMatch: File={file}");
                        }
                    }
                    else
                    {
                        context.Logger.LogError($"{this.Name}.ConditionsCouldNotBeDeserialized: File={file}");
                    }
                }
                else
                {
                    context.Logger.LogError($"{this.Name}.ConditionsWereNotFound: File={file}");
                }
            }
            catch (Exception ex)
            {
                context.CommerceContext.LogException($"{this.Name}.ImportConditionalPolicySetFromFile", ex);
            }

            return false;
        }
    }
}
