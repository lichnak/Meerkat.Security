﻿using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Reflection;

using Common.Logging;

namespace Meerkat.Security.Activities
{
    /// <copydoc cref="IActivityAuthorizer.IsAuthorized" />
    public class ActivityAuthorizer : IActivityAuthorizer
    {
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IDictionary<string, Activity> activities;

        /// <summary>
        /// Creates a new instance of the <see cref="ActivityAuthorizer"/> class.
        /// </summary>
        /// <param name="activities"></param>
        /// <param name="authorized"></param>
        /// <param name="defaultActivity"></param>
        public ActivityAuthorizer(IList<Activity> activities, bool authorized, string defaultActivity = null)
        {
            this.activities = new Dictionary<string, Activity>();
            foreach (var activity in activities)
            {
                this.activities[activity.Name] = activity;
            }

            DefaultAuthorization = authorized;
            DefaultActivity = defaultActivity;
        }

        /// <summary>
        /// Get or set the default authorization value if the authorizer has no opinion.
        /// </summary>
        public bool DefaultAuthorization { get; set; }

        /// <summary>
        /// Gets or sets the default activity to perform authorization against.
        /// </summary>
        public string DefaultActivity { get; set; }

        /// <summary>
        /// Get all the activities supported
        /// </summary>
        public IList<Activity> Activities
        {
            get { return activities.Values.ToList(); }
        }

        /// <copydoc cref="IActivityAuthorizer.IsAuthorized" />
        public AuthorisationReason IsAuthorized(string resource, string action, IPrincipal principal)
        {
            var reason = new AuthorisationReason
            {
                Resource = resource,
                Action = action,
                Principal = principal,
                // We will always make a decision at this level
                NoDecision = false,
                IsAuthorised = DefaultAuthorization
            };

            // Check the activities
            foreach (var activity in FindActivities(resource, action))
            {
                var rs = IsAuthorized(activity, principal);
                if (rs.NoDecision == false)
                {
                    // Ok, we have a decision
                    reason.IsAuthorised = rs.IsAuthorised;
                    if (reason.Resource != rs.Resource || reason.Action != rs.Action)
                    {
                        // Decided based on some other activity, so say what that was
                        reason.PrincipalReason = rs;
                    }

                    break;
                }
            }

            if (!reason.IsAuthorised)
            {
                Logger.InfoFormat("Failed authorization: User '{0}', Resource: '{1}', Action: '{2}'", principal == null ? "<Unknown>" : principal.Identity.Name, resource, action);
            }

            return reason;
        }

        /// <summary>
        /// Check the authorization for an activity against a user
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        public AuthorisationReason IsAuthorized(Activity activity, IPrincipal principal)
        {
            var reason = new AuthorisationReason
            {
                Resource =  activity.Resource,
                Action = activity.Action,
                Principal = principal,
                NoDecision = true,
                IsAuthorised = DefaultAuthorization,
            };

            // Check the denies first, must take precedence over the allows
            if (CheckPermission(activity.Deny, principal, reason))
            {
                // Have an explicit deny.
                reason.NoDecision = false;
                reason.IsAuthorised = false;
            }
            else if (CheckPermission(activity.Allow, principal, reason))
            {
                // Have an explity allow.
                reason.NoDecision = false;
                reason.IsAuthorised = true;
            }
            else if (activity.Default.HasValue)
            {
                // Default authorization on the activity.
                reason.NoDecision = false;
                reason.IsAuthorised = activity.Default.Value;
            }

            return reason;
        }

        private bool CheckPermission(Permission permission, IPrincipal principal, AuthorisationReason reason)
        {
            // Check user over roles
            if (permission.Users.Any(user => principal.Identity.Name == user))
            {
                return true;
            }

            foreach (var role in permission.Roles.Where(principal.IsInRole))
            {
                // Hit due to this role so record it.
                reason.Role = role;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find all activities that have been modelled.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        private IEnumerable<Activity> FindActivities(string resource, string action)
        {
            Activity value = null;

            // Find the closest activity match - resource centric
            foreach (var activity in ActivityExtensions.Activities(resource, action))
            {
                if (activities.TryGetValue(activity, out value))
                {
                    yield return value;
                }
            }

            if (!string.IsNullOrEmpty(DefaultActivity))
            {
                // Attempt to get the default activity.
                if (activities.TryGetValue(DefaultActivity, out value))
                {
                    yield return value;
                }
            }
        }
    }
}