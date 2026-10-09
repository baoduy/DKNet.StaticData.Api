Feature: DKNet StaticData empty service

  @integration
  Scenario Outline: A missing database choice means Postgres
    Given platform-ops <how> the database choice
    When the service starts
    Then the service keeps its data in Postgres

    Examples:
      | how          |
      | leaves out   |
      | leaves blank |

  @integration
  Scenario Outline: The operator's database choice picks the database
    Given platform-ops sets the database choice to "<choice>"
    When the service starts
    Then the service keeps its data in <database>

    Examples:
      | choice    | database   |
      | Postgres  | Postgres   |
      | SqlServer | SQL Server |
      | sqlserver | SQL Server |

  @unit
  Scenario: An unknown database choice stops the service
    Given platform-ops sets the database choice to "MySql"
    When the service starts
    Then the service stops before it serves any request
    And the message names "Postgres" and "SqlServer" as the allowed choices

  @integration
  Scenario Outline: The service brings an empty database up to date at start
    Given an empty <database> database
    And migration at start is turned on
    When the service starts
    Then the database is up to date
    And the database holds no product, purchase order or membership number sequence

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: A second start applies nothing
    Given a <database> database that the service already brought up to date
    When the service starts again
    Then no migration is applied

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: A probe reads the health status without a token
    Given the service runs on a healthy <database> database with token checking on
    When the cluster readiness probe calls the health status route without a token
    Then the answer is "Healthy" with HTTP 200
    And the answer names no check

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: An operator reads the health detail without a token
    Given the service runs on a healthy <database> database with token checking on
    When platform-ops calls the health detail route without a token
    Then the report lists exactly 1 check, the database, as "Healthy"
    And the report shows the check's duration

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: A stopped database makes the service unhealthy
    Given the service runs on <database>
    And the database server is stopped
    When the cluster readiness probe calls the health status route
    Then the answer is "Unhealthy" with HTTP 503
    And the answer names no check and shows no failure text

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: The health detail shows why the database check fails
    Given the service runs on <database>
    And the database server is stopped
    When platform-ops calls the health detail route without a token
    Then the report shows the database check as "Unhealthy"
    And the report shows the check's failure message

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @integration
  Scenario Outline: The service needs only its database
    Given a <database> database and no Redis or message bus
    When the service starts
    Then the health status route answers "Healthy"

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @unit
  Scenario: The service serves only the 2 health routes
    Given the service runs with its default settings
    When the service lists the routes it serves
    Then the list holds only the health status route and the health detail route

  @integration
  Scenario Outline: An address the service does not serve still needs a token
    Given the service runs on <database> with token checking on
    When onboarding-service calls the root address without a token
    Then the service refuses the call as unauthenticated
    And the answer carries no health status

    Examples:
      | database   |
      | Postgres   |
      | SQL Server |

  @unit
  Scenario: Slice 1 has no client package
    Given the service solution built from the template
    When dev-team lists its projects
    Then no client package project is present
