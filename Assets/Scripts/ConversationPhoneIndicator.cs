using UnityEngine;

public class ConversationPhoneIndicator : MonoBehaviour
{
    public Animator phoneButtonAnimator;

    void OnEnable()  => VNEngine.UIManager.OnNodeCompleted += Refresh;
    void OnDisable() => VNEngine.UIManager.OnNodeCompleted -= Refresh;

    void Refresh()
    {
        if (phoneButtonAnimator == null) return;
        bool hasNew = VNEngine.StatsManager.Get_Boolean_Stat("PhoneHasNewActivity");
        phoneButtonAnimator.SetTrigger(hasNew ? "notification" : "default");
    }
}
