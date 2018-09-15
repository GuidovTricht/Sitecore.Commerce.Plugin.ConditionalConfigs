using System.Collections.Generic;
using Sitecore.Commerce.Core;

namespace Sitecore.Commerce.Plugin.ConditionalConfigs.Entities
{
    public class ConditionalPolicySet : PolicySet
    {
        public IDictionary<string, string> Conditions { get; set; }
    }
}
