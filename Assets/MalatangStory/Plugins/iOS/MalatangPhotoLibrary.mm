#include <TargetConditionals.h>
#if TARGET_OS_IOS
#import <Photos/Photos.h>
#import <UIKit/UIKit.h>
extern "C" void UnitySendMessage(const char *, const char *, const char *);
extern "C" void MalatangSavePhoto(const char *path, const char *callbackObject)
{
    NSString *file = [NSString stringWithUTF8String:path];
    NSString *target = [NSString stringWithUTF8String:callbackObject];
    void (^reply)(const char *) = ^(const char *status) {
        NSString *result = [NSString stringWithUTF8String:status];
        dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(target.UTF8String, "OnPhotoSaved", result.UTF8String); });
    };
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status) {
        if (status != PHAuthorizationStatusAuthorized && status != PHAuthorizationStatusLimited) { reply("denied"); return; }
        UIImage *image = [UIImage imageWithContentsOfFile:file];
        if (image == nil) { reply("error"); return; }
        [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{ [PHAssetChangeRequest creationRequestForAssetFromImage:image]; }
            completionHandler:^(BOOL success, NSError *error) { reply(success ? "ok" : "error"); }];
    }];
}
#endif
