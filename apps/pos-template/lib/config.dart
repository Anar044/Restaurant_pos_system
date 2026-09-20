class AppConfig {
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://127.0.0.1:8080',
  );

  static const posAgentBaseUrl = String.fromEnvironment(
    'POS_AGENT_BASE_URL',
    defaultValue: 'http://127.0.0.1:8791',
  );

  static const restaurantDisplayName = String.fromEnvironment(
    'RESTAURANT_NAME',
    defaultValue: 'Demo Restaurant',
  );

  static const currencyCode = String.fromEnvironment(
    'CURRENCY_CODE',
    defaultValue: 'AZN',
  );

  // Development identifiers only. Later both come from device provisioning.
  static const developmentRestaurantId =
      '11111111-1111-1111-1111-111111111111';

  static const posDeviceId = String.fromEnvironment(
    'POS_DEVICE_ID',
    defaultValue: '01a0b072-5a20-7a10-add0-4f890c477588',
  );
}
