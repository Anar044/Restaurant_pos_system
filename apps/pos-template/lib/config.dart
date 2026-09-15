class AppConfig {
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://127.0.0.1:8080',
  );

  // Development seed restaurant only. Later this comes from device provisioning.
  static const developmentRestaurantId =
      '11111111-1111-1111-1111-111111111111';
}
