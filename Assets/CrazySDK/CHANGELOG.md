# 5.39.0

- `SubmitScore` method now has overloads accepting the plain score as a `long` or a `double`.

# 5.38.0

- Add `IsOfflineMode` property and `ReportGameCompletedPercentage` method to `Game` module.

# 5.37.0

- Remove the undocumented `PrefetchAd` method from `Ad` module.

# 5.36.0

- Add `GetUserToken` and `GetUserTokenAsync` overloads accepting an `InGameAuthSigningKeyType`, to choose the key used to sign the user token.
- Custom builds now support progressive asset loading.

# 5.35.0

- Add `GetCatalogItems`, `GetPlayerInventory` and `ConsumeItem` methods to `Store` module.

# 5.34.0

- Add `Quality` property, `GameQuality` enum and `AddQualityChangeListener`/`RemoveQualityChangeListener` methods to `Game` module.

# 5.33.0

- Add `Store` module with `BuyItem` and `SetSandbox` methods.

# 5.32.0

- Add logs for debugging banners that are partially visible or off-screen.

# 5.31.0

- `SubmitScore` method now has an overload accepting the plain score as a second argument.

# 5.30.0

- Add `SetGameContext` and `ClearGameContext` methods to `Game` module.

# 5.29.0

- Add `__dangerousUserId` to the user.

# 5.28.0

- `UpdateRoom` method now accepts an input object with the following parameters: `RoomId`, `IsJoinable`, `InviteParams`.

# 5.27.0

- Add `InviteParams` to game module for accessing invite parameters

# SDK 5.26.0

- Add `AddJoinRoomListener` and `RemoveJoinRoomListener` methods to Game module

# SDK 5.25.0

- Add `UpdateRoom` and `LeftRoom` methods to the `Game` module.

# SDK 5.24.0

- Deprecate `IsQaTool`

# SDK 5.23.0

- Add `ListFriends` SDK methods to User module

# SDK 5.22.0

- Add `AddSettingsChangeListener` and `RemoveSettingsChangeListener` method to Game module
- Add `muteAudio` property to game settings

# Custom build 1.3.0

- Toggle "Runs on mobile web". This will skip generating some files required for mobile, reducing the build time.

# SDK 5.21.0

- Add leaderboard score submission

# SDK 5.20.0

- Add custom build feature
- Add async versions for methods with callbacks

# SDK 5.19.0

- Show/hide cursor when adblock popup is opened/closed

# SDK 5.18.0

- Add `locale` to SystemInfo

# SDK 5.17.0

- Add `applicationType` to SystemInfo
