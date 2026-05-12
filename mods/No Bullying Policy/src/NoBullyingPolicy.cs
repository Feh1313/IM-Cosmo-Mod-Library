using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;

namespace NoBullyingPolicyMod
{
    internal static class C
    {
        // Use integers mapped to the JSON so Enum.Parse doesn't crash the game
        internal const int PolicyTypeBullying = 100;
        internal const int PolicyValueEnabled = 1000;
        internal const int PolicyValueDisabled = 1001;

        internal const string RelationshipsTargetMethod = "Do_Bullying";
        internal const string PolicyObjName="Policy_Bullying";
        internal const string HarmonyUITargetMethodPatch="Start";
    }

    // 1. Localization Hook: Translates the JSON keys into text right after the policy loads
    [HarmonyPatch(typeof(policies.value), nameof(policies.value.SetData))]
    internal static class PolicyValue_SetData_Patch
    {
        private static void Postfix(policies.value __instance)
        {
            // Only attempt to translate our custom policy to prevent interfering with base game
            if ((int)__instance.Type == C.PolicyTypeBullying)
            {
                if (!string.IsNullOrEmpty(__instance.Title))
                {
                    __instance.Title = ModLocalization.GetRaw(__instance.Title);
                }
                
                if (!string.IsNullOrEmpty(__instance.Tooltip_Value))
                {
                    __instance.Tooltip_Value = ModLocalization.GetRaw(__instance.Tooltip_Value);
                }
            }
        }
    }

    // 2. Prevent new bullying
    [HarmonyPatch(typeof(Relationships._clique), nameof(Relationships._clique.AddBulliedGirl))]
    internal static class Clique_AddBulliedGirl_Patch
    {
        private static bool Prefix()
        {
            // Retrieve our custom policy using the integer
            policies.value activePolicy = policies.GetSelectedPolicyValue((policies._type)C.PolicyTypeBullying);
            
            // 1001 is the "No Bullying" option
            if (activePolicy != null && (int)activePolicy.Value == C.PolicyValueDisabled)
            {
                // Skip the original method, preventing new girls from being targeted
                return false; 
            }
            return true;
        }
    }

    // 3. Stop ongoing bullying and prevent weekly penalties
    [HarmonyPatch(typeof(Relationships), C.RelationshipsTargetMethod)]
    internal static class Relationships_Do_Bullying_Patch
    {
        private static bool Prefix()
        {
            policies.value activePolicy = policies.GetSelectedPolicyValue((policies._type)C.PolicyTypeBullying);

            if (activePolicy != null && (int)activePolicy.Value == C.PolicyValueDisabled)
            {
                // Loop through all cliques and forcefully end any ongoing bullying
                foreach (Relationships._clique clique in Relationships.Cliques)
                {
                    // Iterate backward because StopBullying modifies the list
                    for (int i = clique.Bullied_Girls.Count - 1; i >= 0; i--)
                    {
                        clique.StopBullying(clique.Bullied_Girls[i]);
                    }
                }
                
                // Skip the original method so no weekly mental stamina damage is applied
                return false; 
            }
            
            // If the policy is set to "Default", let the base game handle the bullying
            return true; 
        }
    }

    [HarmonyPatch(typeof(policies), C.HarmonyUITargetMethodPatch)]
    internal static class Policies_UI_Injection_Patch
    {
        private static void Postfix(policies __instance)
        {
            // Find the container where policy rows are held
            // In the base game, this is usually inside a ScrollView content object
            GameObject policyPrefab = null;
            Transform container = null;

            // Find an existing policy object to use as a template
            Policy existingPolicy = GameObject.FindObjectOfType<Policy>();
            if (existingPolicy != null)
            {
                policyPrefab = existingPolicy.gameObject;
                container = existingPolicy.transform.parent;
            }

            if (policyPrefab != null && container != null)
            {
                // Create our new UI row
                GameObject newPolicyObj = GameObject.Instantiate(policyPrefab, container);
                newPolicyObj.name = C.PolicyObjName;

                // Configure the Policy component
                Policy policyScript = newPolicyObj.GetComponent<Policy>();
                
                // Force it to point to our custom ID (100)
                policyScript.type = (policies._type)C.PolicyTypeBullying;
                
                // Re-run the Render to apply the localized text
                policyScript.Render();
            }
        }
    }
}