#import <UIKit/UIKit.h>

extern "C" {
    void _UnityHapticImpactLight() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            [gen prepare];
            [gen impactOccurred];
        }
    }
    
    void _UnityHapticImpactMedium() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            [gen prepare];
            [gen impactOccurred];
        }
    }
    
    void _UnityHapticImpactHeavy() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *gen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            [gen prepare];
            [gen impactOccurred];
        }
    }
    
    void _UnityHapticSelection() {
        if (@available(iOS 10.0, *)) {
            UISelectionFeedbackGenerator *gen = [[UISelectionFeedbackGenerator alloc] init];
            [gen prepare];
            [gen selectionChanged];
        }
    }
    
    void _UnityHapticSuccess() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
            [gen prepare];
            [gen notificationOccurred:UINotificationFeedbackTypeSuccess];
        }
    }
}
