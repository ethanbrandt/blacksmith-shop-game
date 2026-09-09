using UnityEngine;

/// <summary>One station owns input at a time. Camera masks are authored on the rigs, not changed globally.</summary>
public static class StationSessionCoordinator
{
	static Object sessionOwner;

	public static bool IsActive => sessionOwner != null;

	public static bool CanAcquire(Object requestingOwner) => sessionOwner == null || sessionOwner == requestingOwner;

	public static bool TryAcquire(Object requestingOwner)
	{
		if (requestingOwner == null || !CanAcquire(requestingOwner))
			return false;

		sessionOwner = requestingOwner;
		return true;
	}

	public static void Release(Object requestingOwner)
	{
		if (sessionOwner == requestingOwner)
			sessionOwner = null;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	static void Reset() => sessionOwner = null;
}
