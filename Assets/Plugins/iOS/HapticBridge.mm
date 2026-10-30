#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *s_lightGen = nil;
static UIImpactFeedbackGenerator *s_mediumGen = nil;
static UIImpactFeedbackGenerator *s_heavyGen = nil;
static UISelectionFeedbackGenerator *s_selectionGen = nil;
static UINotificationFeedbackGenerator *s_notificationGen = nil;

extern "C" {
    void _UnityHapticImpactLight() {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!s_lightGen) {
                    s_lightGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                }
                [s_lightGen prepare];
                [s_lightGen impactOccurred];
            });
        }
    }
    
    void _UnityHapticImpactMedium() {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!s_mediumGen) {
                    s_mediumGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                }
                [s_mediumGen prepare];
                [s_mediumGen impactOccurred];
            });
        }
    }
    
    void _UnityHapticImpactHeavy() {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!s_heavyGen) {
                    s_heavyGen = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                }
                [s_heavyGen prepare];
                [s_heavyGen impactOccurred];
            });
        }
    }
    
    void _UnityHapticSelection() {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!s_selectionGen) {
                    s_selectionGen = [[UISelectionFeedbackGenerator alloc] init];
                }
                [s_selectionGen prepare];
                [s_selectionGen selectionChanged];
            });
        }
    }
    
    void _UnityHapticSuccess() {
        if (@available(iOS 10.0, *)) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!s_notificationGen) {
                    s_notificationGen = [[UINotificationFeedbackGenerator alloc] init];
                }
                [s_notificationGen prepare];
                [s_notificationGen notificationOccurred:UINotificationFeedbackTypeSuccess];
            });
        }
    }
}
